using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using PocketDesk.Shared.Widgets;

namespace PocketDesk.Verification;

/// <summary>
/// 端到端验收探针：启动真实宿主，检查窗口样式、命中测试、命名管道 IPC、
/// 组件 Schema 与数据操作，然后退出宿主。对应 PRD 的 AC-01/04/05/06/07。
/// </summary>
internal static class Program
{
    private static int _failures;
    private static int _passes;
    private static string _home = string.Empty;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var hostExe = args.Length > 0 && File.Exists(args[0])
            ? Path.GetFullPath(args[0])
            : Environment.GetEnvironmentVariable("POCKETDESK_HOST_EXE")
              ?? Path.Combine(workspaceRoot, "src", "PocketDesk.Host", "bin", "Debug", "net10.0", "PocketDesk.Host.exe");

        if (!File.Exists(hostExe))
        {
            Console.WriteLine($"FAIL 找不到宿主可执行文件：{hostExe}");
            return 2;
        }

        _home = Environment.GetEnvironmentVariable("POCKETDESK_VERIFY_HOME")
                ?? Path.Combine(Path.GetTempPath(), "PocketDesk-New-Verify", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        var keepHome = Environment.GetEnvironmentVariable("POCKETDESK_VERIFY_KEEP") == "1";
        Console.WriteLine($"宿主：{hostExe}");
        Console.WriteLine($"隔离数据目录：{_home}");

        var startInfo = new ProcessStartInfo(hostExe, Environment.GetEnvironmentVariable("POCKETDESK_VERIFY_NO_DEMO") == "1" ? string.Empty : "--demo")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostExe)!,
        };
        startInfo.Environment["POCKETDESK_HOME"] = _home;

        using var host = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动宿主。");
        try
        {
            var window = await WaitForWindowAsync(host.Id, TimeSpan.FromSeconds(25));
            Check("宿主窗口已创建", window != IntPtr.Zero, $"hwnd=0x{window:X}");
            Console.WriteLine($"INFO  冷启动到窗口可见：{(DateTime.Now - host.StartTime):mm\\:ss\\.fff}；" +
                              $"工作集 {host.WorkingSet64 / 1024 / 1024} MB，私有内存 {host.PrivateMemorySize64 / 1024 / 1024} MB");

            if (window != IntPtr.Zero)
            {
                // 等待 Avalonia 完成首帧布局与卡片排列后再做命中测试。
                await Task.Delay(1800);
                VerifyWindow(window, host.Id);
            }

            SettingsSnapshot? snapshot = null;
            for (var attempt = 0; attempt < 10 && snapshot is null; attempt++)
            {
                try
                {
                    snapshot = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
                }
                catch
                {
                    await Task.Delay(500);
                }
            }

            Check("命名管道 IPC 可连接", snapshot is not null);
            if (snapshot is not null && window != IntPtr.Zero)
            {
                await VerifyIpcAsync(snapshot, window);
            }
        }
        finally
        {
            try
            {
                if (!host.HasExited)
                {
                    await SendAsync<OperationResult>(IpcCommands.Shutdown, null, required: false);
                    if (!host.WaitForExit(4000))
                    {
                        host.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                try
                {
                    host.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }
            }

            try
            {
                if (!keepHome)
                {
                    Directory.Delete(_home, recursive: true);
                }
            }
            catch
            {
                // SQLite 句柄可能延迟释放。
            }
        }

        Console.WriteLine();
        Console.WriteLine($"结果：{_passes} 通过，{_failures} 失败。");
        return _failures == 0 ? 0 : 1;
    }

    private static void VerifyWindow(IntPtr hwnd, int processId)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        Check("窗口属于宿主进程", pid == processId);

        var exStyle = GetWindowLongPtr(hwnd, -20).ToInt64();
        Check("WS_EX_TOOLWINDOW：不进入任务栏 / Alt+Tab", (exStyle & 0x80) != 0, $"exstyle=0x{exStyle:X}");
        Check("WS_EX_NOACTIVATE：默认不抢系统焦点", (exStyle & 0x08000000) != 0, $"exstyle=0x{exStyle:X}");
        Check("不是置顶窗口（普通应用可覆盖）", (exStyle & 0x8) == 0, $"exstyle=0x{exStyle:X}");
        Check("启动后未成为前台窗口", GetForegroundWindow() != hwnd);

        var foreground = GetForegroundWindow();
        var hostIndex = ZOrderIndexOf(hwnd);
        var foregroundIndex = ZOrderIndexOf(foreground);
        Check("位于普通前台窗口之下（AC-04）", hostIndex > 0 && foregroundIndex > 0 && hostIndex > foregroundIndex,
            $"host={hostIndex}, foreground={foregroundIndex}");

        var progman = FindWindow("Progman", null);
        Check("owner 为桌面 Progman（壁纸之上）", GetWindowLongPtr(hwnd, -8) == progman,
            $"owner=0x{GetWindowLongPtr(hwnd, -8).ToInt64():X}, progman=0x{progman.ToInt64():X}");

        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
        var client = new RECT();
        GetClientRect(hwnd, out client);
        Check("组件层覆盖整个工作区", client.Right > 800 && client.Bottom > 500,
            $"{client.Right}x{client.Bottom} px @ {scale:0.##}x");

        // Demo 收纳盒“工作资料”位于 DIP (70,60)，尺寸 430x470；卡片头部是明确的交互区。
        var cardPoint = ToScreen(hwnd, 100, 80, scale);
        var cardHit = SendMessage(hwnd, 0x0084, IntPtr.Zero, MakeLParam(cardPoint.X, cardPoint.Y)).ToInt32();
        Check("WM_NCHITTEST：卡片区域返回 HTCLIENT", cardHit == 1, $"result={cardHit}");

        // 卡片之间的空白必须整体穿透到桌面/下层应用。
        var gapPoint = ToScreen(hwnd, 510, 200, scale);
        var gapHit = SendMessage(hwnd, 0x0084, IntPtr.Zero, MakeLParam(gapPoint.X, gapPoint.Y)).ToInt32();
        Check("WM_NCHITTEST：卡片间空白返回 HTTRANSPARENT", gapHit == -1, $"result={gapHit}");

        var emptyPoint = ToScreen(hwnd, 700, 700, scale);
        var emptyHit = SendMessage(hwnd, 0x0084, IntPtr.Zero, MakeLParam(emptyPoint.X, emptyPoint.Y)).ToInt32();
        Check("WM_NCHITTEST：桌面空白返回 HTTRANSPARENT", emptyHit == -1, $"result={emptyHit}");

        var before = GetForegroundWindow();
        var mouseActivate = SendMessage(hwnd, 0x0021, hwnd, MakeLParam(1, 1)).ToInt32();
        Check("WM_MOUSEACTIVATE：点击卡片不激活窗口", mouseActivate == 3, $"result={mouseActivate}");
        Check("WM_MOUSEACTIVATE 后前台窗口未改变", GetForegroundWindow() == before);

        // 合成点击搜索框，验证“视觉共存 + 按需键盘激活”。
        var inputResult = ProbeInputFocus(hwnd, scale);
        Check("点击搜索框后临时获取键盘焦点（AC-03）", inputResult.Activated,
            $"foreground 0x{inputResult.ForegroundBefore.ToInt64():X} -> 0x{inputResult.ForegroundDuring.ToInt64():X}");
        Check("Esc 后焦点交还之前的应用（AC-03）", inputResult.Restored,
            $"foreground 0x{inputResult.ForegroundAfter.ToInt64():X}");
    }

    private static (bool Activated, bool Restored, IntPtr ForegroundBefore, IntPtr ForegroundDuring, IntPtr ForegroundAfter) ProbeInputFocus(
        IntPtr hwnd,
        double scale)
    {
        var before = GetForegroundWindow();
        var x = (int)Math.Round(200 * scale);
        var y = (int)Math.Round(122 * scale);
        var lParam = MakeLParam(x, y);
        SendMessage(hwnd, 0x0200, IntPtr.Zero, lParam);
        SendMessage(hwnd, 0x0201, new IntPtr(1), lParam);
        SendMessage(hwnd, 0x0202, IntPtr.Zero, lParam);
        Thread.Sleep(500);
        var during = GetForegroundWindow();
        var exStyle = GetWindowLongPtr(hwnd, -20).ToInt64();
        var activated = during == hwnd && (exStyle & 0x08000000) == 0;

        SendMessage(hwnd, 0x0100, new IntPtr(0x1B), IntPtr.Zero); // WM_KEYDOWN VK_ESCAPE
        SendMessage(hwnd, 0x0101, new IntPtr(0x1B), IntPtr.Zero); // WM_KEYUP
        Thread.Sleep(500);
        var after = GetForegroundWindow();
        var restored = after == before && (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x08000000) != 0;
        return (activated, restored, before, during, after);
    }

    private static async Task VerifyIpcAsync(SettingsSnapshot snapshot, IntPtr hwnd)
    {
        Check("快照包含 3 个演示收纳盒", snapshot.Boxes.Count == 3, $"count={snapshot.Boxes.Count}");
        Check("快照包含 4 个组件实例（3 盒 + 天气）", snapshot.Widgets.Count == 4, $"count={snapshot.Widgets.Count}");
        Check("组件层包含 3 种收纳盒模式",
            snapshot.Boxes.Select(b => b.Type).Distinct().OrderBy(t => t).SequenceEqual(new[] { "Mapped", "Normal", "Target" }));

        var weatherType = snapshot.WidgetTypes.FirstOrDefault(t => t.TypeId == AppInfo.WeatherWidgetId);
        Check("插件组件被宿主发现（AC-06）", weatherType is not null && !weatherType.IsBuiltIn);
        Check("插件 Schema 通过 IPC 暴露 5 个配置项", weatherType?.Schema.Fields.Count == 5,
            $"fields={weatherType?.Schema.Fields.Count}");

        var boxType = snapshot.WidgetTypes.FirstOrDefault(t => t.TypeId == AppInfo.BoxWidgetId);
        Check("内置收纳盒组件已注册", boxType is { IsBuiltIn: true });
        Check("收纳盒 Schema 包含视图/数量/排序", boxType?.Schema.Fields.Count == 3,
            $"fields={boxType?.Schema.Fields.Count}");

        var originalRadius = snapshot.Settings.DefaultCornerRadius;
        var updated = snapshot.Settings.Clone();
        updated.DefaultCornerRadius = 24;
        await SendAsync<OperationResult>(IpcCommands.UpdateSettings, new UpdateSettingsPayload { Settings = updated });
        var afterUpdate = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
        Check("设置界面修改圆角后宿主即时生效（AC-07）",
            afterUpdate is not null && Math.Abs(afterUpdate.Settings.DefaultCornerRadius - 24) < 0.01,
            $"radius={afterUpdate?.Settings.DefaultCornerRadius}");

        updated.DefaultCornerRadius = originalRadius;
        await SendAsync<OperationResult>(IpcCommands.UpdateSettings, new UpdateSettingsPayload { Settings = updated });

        var workBox = snapshot.Widgets.FirstOrDefault(w => w.WidgetTypeId == AppInfo.BoxWidgetId &&
                                                           w.DisplayName == "工作资料");
        Check("收纳盒实例也以 Widget 形式暴露", workBox is not null);
        if (workBox is not null)
        {
            var boxSettings = new Dictionary<string, string>(workBox.Settings, StringComparer.OrdinalIgnoreCase)
            {
                ["view"] = "list",
                ["showItemCount"] = "false",
                ["sortField"] = "type",
            };
            await SendAsync<OperationResult>(IpcCommands.UpdateWidgetSettings, new UpdateWidgetSettingsPayload
            {
                InstanceId = workBox.Id,
                Settings = boxSettings,
            });

            var afterBoxSettings = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
            var changed = afterBoxSettings?.Widgets.FirstOrDefault(w => w.Id == workBox.Id);
            Check("收纳盒配置通过 Schema 键值即时生效", changed?.GetSetting("view") == "list",
                $"view={changed?.GetSetting("view")}");

            boxSettings["view"] = "grid";
            boxSettings["showItemCount"] = "true";
            boxSettings["sortField"] = "name";
            await SendAsync<OperationResult>(IpcCommands.UpdateWidgetSettings, new UpdateWidgetSettingsPayload
            {
                InstanceId = workBox.Id,
                Settings = boxSettings,
            });
        }

        var created = await SendAsync<WidgetInstance>(IpcCommands.CreateBox, new CreateBoxPayload
        {
            Name = "验收收纳盒",
            Type = "Normal",
        });
        Check("IPC 创建收纳盒", created is not null);
        var afterCreate = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
        Check("新收纳盒出现在快照中", afterCreate?.Boxes.Any(b => b.Id == created!.Id) == true);

        if (created is not null)
        {
            await SendAsync<OperationResult>(IpcCommands.DeleteBox, new IdPayload { Id = created.Id });
            var afterDelete = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
            Check("IPC 删除收纳盒", afterDelete?.Boxes.Count == 3);
        }

        var newWeather = await SendAsync<WidgetInstance>(IpcCommands.AddWidget, new AddWidgetPayload
        {
            TypeId = AppInfo.WeatherWidgetId,
        });
        Check("宿主可在不改代码的情况下挂载新组件实例（AC-06）", newWeather is not null);
        if (newWeather is not null)
        {
            await SendAsync<OperationResult>(IpcCommands.UpdateWidgetSettings, new UpdateWidgetSettingsPayload
            {
                InstanceId = newWeather.Id,
                Settings = new Dictionary<string, string>
                {
                    ["city"] = "深圳",
                    ["refreshMinutes"] = "15",
                    ["units"] = "c",
                    ["showFeelsLike"] = "true",
                    ["accent"] = "#7C6BFF",
                },
            });

            var withWeather = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
            var weatherInstance = withWeather?.Widgets.FirstOrDefault(w => w.Id == newWeather.Id);
            Check("组件设置通过 Schema 键值持久化", weatherInstance?.GetSetting("city") == "深圳",
                $"city={weatherInstance?.GetSetting("city")}");

            await SendAsync<OperationResult>(IpcCommands.RemoveWidget, new IdPayload { Id = newWeather.Id });
            var afterRemove = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot);
            Check("移除组件后快照恢复", afterRemove?.Widgets.Count == 4);
        }

        var diagnostics = await SendAsync<DiagnosticsResult>(IpcCommands.ExportDiagnostics);
        Check("诊断日志导出", diagnostics is not null && File.Exists(diagnostics.Path));

        var tile = await SendAsync<OperationResult>(IpcCommands.TileBoxes);
        Check("网格排列命令执行", tile is not null);

        var ping = await SendAsync<JsonElement>(IpcCommands.Ping);
        Check("Ping 往返成功", ping is { ValueKind: JsonValueKind.Object });
    }

    private static async Task<IntPtr> WaitForWindowAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hwnd = FindLargestWindow(processId);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }

            await Task.Delay(250);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindLargestWindow(int processId)
    {
        var best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var className = new StringBuilder(128);
            GetClassName(hwnd, className, className.Capacity);
            if (!className.ToString().StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static int ZOrderIndexOf(IntPtr target)
    {
        if (target == IntPtr.Zero)
        {
            return -1;
        }

        var index = 0;
        var found = -1;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            index++;
            if (hwnd == target)
            {
                found = index;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static async Task<T?> SendAsync<T>(string command, object? payload = null, bool required = true)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using var pipe = new NamedPipeClientStream(".", AppInfo.HostPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1200, timeout.Token);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);

                var request = new IpcRequest
                {
                    Command = command,
                    Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.Compact),
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Compact));
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    throw new IOException("宿主关闭了连接。");
                }

                var response = JsonSerializer.Deserialize<IpcResponse>(line, JsonDefaults.Compact)
                               ?? throw new InvalidOperationException("无法解析响应。");
                if (!response.Ok)
                {
                    throw new InvalidOperationException(response.Error ?? "操作失败。");
                }

                return response.PayloadAs<T>();
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(250);
            }
        }

        if (required)
        {
            throw last ?? new InvalidOperationException("IPC 请求失败。");
        }

        return default;
    }

    private static POINT ToScreen(IntPtr hwnd, double xDip, double yDip, double scale)
    {
        var point = new POINT
        {
            X = (int)Math.Round(xDip * scale),
            Y = (int)Math.Round(yDip * scale),
        };
        ClientToScreen(hwnd, ref point);
        return point;
    }

    private static IntPtr MakeLParam(int x, int y) => new((y << 16) | (x & 0xFFFF));

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passes++;
            Console.WriteLine($"PASS  {name}{(detail is null ? string.Empty : $"  ({detail})")}");
        }
        else
        {
            _failures++;
            Console.WriteLine($"FAIL  {name}{(detail is null ? string.Empty : $"  ({detail})")}");
        }
    }

    // ---------- Win32 ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
}
