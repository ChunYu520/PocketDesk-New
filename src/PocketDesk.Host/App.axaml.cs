using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;

namespace PocketDesk.Host;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HostServices? _host;
    private string? _screenshotPath;
    private bool _demo;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var args = desktop.Args ?? Environment.GetCommandLineArgs();

        _singleInstance = new Mutex(true, AppInfo.HostMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            NotifyExistingInstance();
            desktop.Shutdown();
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            LogService.Initialize();
            Database.Initialize();
            AppSettingsService.Load();
            var recovery = RecoveryService.RecoverOnStartup();

            _host = new HostServices();
            _host.Initialize();

            var themeArg = GetOption(args, "--theme");
            if (themeArg is "Light" or "Dark" or "System")
            {
                AppSettingsService.Current.Theme = themeArg;
                _host.Theme.Apply(AppSettingsService.Current);
            }

            _demo = args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
            _screenshotPath = GetOption(args, "--screenshot");

            if (_demo || _screenshotPath is not null)
            {
                DemoData.EnsureSeeded(_host);
            }
            else if (BoxService.GetBoxes().Count == 0)
            {
                BoxService.CreateBox("收纳盒 1", BoxType.Normal, null, new WindowPlacement
                {
                    X = 120,
                    Y = 100,
                    Width = 400,
                    Height = 380,
                });
            }

            _host.ViewModel.Load(recovery);

            var window = new MainWindow(_host);
            _host.AttachWindow(window);
            desktop.MainWindow = window;
            window.Show();

            if (_screenshotPath is null)
            {
                _host.Start();
            }

            if (recovery.HasIssues)
            {
                DispatcherTimer.RunOnce(
                    () => _host.ShowToast(recovery.ToDisplayString()),
                    TimeSpan.FromSeconds(1.2));
            }

            if (_screenshotPath is not null)
            {
                DispatcherTimer.RunOnce(() => CaptureScreenshotAsync(_screenshotPath), TimeSpan.FromSeconds(2.4));
            }

            LogService.Info($"PocketDesk-New 启动完成（demo={_demo}, screenshot={_screenshotPath is not null}）");
        }
        catch (Exception ex)
        {
            LogService.Error("宿主启动失败", ex);
            ShowFatalError(desktop, ex);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }

    private async void CaptureScreenshotAsync(string path)
    {
        try
        {
            var window = _host?.Window;
            if (window is null)
            {
                return;
            }

            await Task.Delay(400);
            var width = Math.Max(1, (int)Math.Round(window.ClientSize.Width));
            var height = Math.Max(1, (int)Math.Round(window.ClientSize.Height));
            var dpi = new Vector(96, 96);
            var pixelSize = new PixelSize(width, height);

            var render = new RenderTargetBitmap(pixelSize, dpi);
            render.Render(window);

            var composite = new RenderTargetBitmap(pixelSize, dpi);
            using (var ctx = composite.CreateDrawingContext())
            {
                var background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#101A2E"), 0),
                        new GradientStop(Color.Parse("#1B1638"), 0.45),
                        new GradientStop(Color.Parse("#0B2430"), 1),
                    },
                };
                ctx.FillRectangle(background, new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height));

                // 模拟壁纸上的柔和光斑，便于观察半透明卡片材质。
                ctx.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(40, 76, 194, 255)),
                    null,
                    new Point(window.ClientSize.Width * 0.18, window.ClientSize.Height * 0.22),
                    window.ClientSize.Width * 0.28,
                    window.ClientSize.Height * 0.32);
                ctx.DrawEllipse(
                    new SolidColorBrush(Color.FromArgb(36, 160, 120, 255)),
                    null,
                    new Point(window.ClientSize.Width * 0.78, window.ClientSize.Height * 0.72),
                    window.ClientSize.Width * 0.3,
                    window.ClientSize.Height * 0.3);

                ctx.DrawImage(render, new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            composite.Save(path);
            LogService.Info($"截图已保存：{path}");
            Console.WriteLine($"SCREENSHOT_SAVED {Path.GetFullPath(path)}");
        }
        catch (Exception ex)
        {
            LogService.Error("截图失败", ex);
            Console.WriteLine($"SCREENSHOT_FAILED {ex.Message}");
        }
        finally
        {
            _host?.Shutdown();
        }
    }

    private static void NotifyExistingInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", AppInfo.HostPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(800);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            var request = new IpcRequest { Command = IpcCommands.ShowAll };
            writer.WriteLine(JsonSerializer.Serialize(request, JsonDefaults.Compact));
        }
        catch
        {
            // 已有实例可能正在退出；静默结束。
        }
    }

    private static void ShowFatalError(IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        var window = new Window
        {
            Title = "PocketDesk-New 启动失败",
            Width = 560,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.Parse("#15171E")),
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "PocketDesk-New 无法启动",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                    },
                    new TextBlock
                    {
                        Text = ex.Message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#B8FFFFFF")),
                    },
                    new TextBlock
                    {
                        Text = $"日志：{AppPaths.LogsDir}",
                        Foreground = new SolidColorBrush(Color.Parse("#8AFFFFFF")),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
        desktop.MainWindow = window;
        window.Show();
    }
}
