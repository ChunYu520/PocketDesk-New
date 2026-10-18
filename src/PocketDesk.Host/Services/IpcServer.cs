using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using PocketDesk.Core;
using PocketDesk.Host.ViewModels;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using PocketDesk.Shared.Widgets;

namespace PocketDesk.Host.Services;

/// <summary>
/// 命名管道 IPC 服务端。WinUI 3 设置界面与宿主之间只交换 Shared.Contracts，
/// 双方不引用对方的程序集，也不直接操作 UI 对象。
/// </summary>
public sealed class IpcServer : IDisposable
{
    private readonly HostServices _host;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _clients = new();
    private NamedPipeServerStream? _server;
    private Task? _acceptLoop;

    public IpcServer(HostServices host)
    {
        _host = host;
    }

    public void Start()
    {
        _acceptLoop = Task.Run(AcceptLoopAsync);
        LogService.Info($"IPC 已启动：{AppInfo.HostPipeName}");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    AppInfo.HostPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                _server = server;
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var client = server;
                server = null;
                _clients.RemoveAll(t => t.IsCompleted);
                _clients.Add(Task.Run(() => HandleClientAsync(client)));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Warn($"IPC 接受连接失败：{ex.GetType().Name}: {ex.Message}");
                await Task.Delay(500, _cts.Token).ConfigureAwait(false);
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            try
            {
                while (!_cts.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    IpcResponse response;
                    try
                    {
                        var request = JsonSerializer.Deserialize<IpcRequest>(line, JsonDefaults.Compact)
                                      ?? throw new InvalidOperationException("请求为空。");
                        response = await Dispatcher.UIThread.InvokeAsync(() => HandleAsync(request));
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("IPC 请求处理失败", ex);
                        response = IpcResponse.Failure(string.Empty, ex.Message);
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonDefaults.Compact)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出。
            }
            catch (Exception ex)
            {
                LogService.Warn($"IPC 客户端异常：{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private Task<IpcResponse> HandleAsync(IpcRequest request)
    {
        try
        {
            var result = request.Command switch
            {
                IpcCommands.Ping => IpcResponse.Success(request.Id, new { pong = true, version = AppInfo.Version }),
                IpcCommands.GetSnapshot => IpcResponse.Success(request.Id, _host.CreateSnapshot()),
                IpcCommands.UpdateSettings => UpdateSettings(request),
                IpcCommands.AddWidget => AddWidget(request),
                IpcCommands.RemoveWidget => RemoveWidget(request),
                IpcCommands.SetWidgetVisible => SetWidgetVisible(request),
                IpcCommands.ReorderWidgets => ReorderWidgets(request),
                IpcCommands.UpdateWidgetSettings => UpdateWidgetSettings(request),
                IpcCommands.CreateBox => CreateBox(request),
                IpcCommands.RenameBox => RenameBox(request),
                IpcCommands.DeleteBox => DeleteBox(request),
                IpcCommands.SetBoxVisible => SetBoxVisible(request),
                IpcCommands.ReorderBoxes => ReorderBoxes(request),
                IpcCommands.TileBoxes => TileBoxes(request),
                IpcCommands.ShowAll => ShowAll(request),
                IpcCommands.HideAll => HideAll(request),
                IpcCommands.OpenDataDirectory => OpenPath(request, AppPaths.Root),
                IpcCommands.OpenLogDirectory => OpenPath(request, AppPaths.LogsDir),
                IpcCommands.ExportDiagnostics => ExportDiagnostics(request),
                IpcCommands.Shutdown => Shutdown(request),
                _ => IpcResponse.Failure(request.Id, $"未知命令：{request.Command}"),
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            LogService.Warn($"IPC 命令失败 {request.Command}：{ex.Message}");
            return Task.FromResult(IpcResponse.Failure(request.Id, ex.Message));
        }
    }

    private IpcResponse UpdateSettings(IpcRequest request)
    {
        var payload = request.PayloadAs<UpdateSettingsPayload>();
        if (payload?.Settings is null)
        {
            return IpcResponse.Failure(request.Id, "缺少设置内容。");
        }

        _host.UpdateSettings(payload.Settings);
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse AddWidget(IpcRequest request)
    {
        var payload = request.PayloadAs<AddWidgetPayload>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.TypeId))
        {
            return IpcResponse.Failure(request.Id, "缺少组件类型。");
        }

        var card = _host.ViewModel.AddWidget(payload.TypeId, payload.DisplayName);
        return IpcResponse.Success(request.Id, card?.Instance);
    }

    private IpcResponse RemoveWidget(IpcRequest request)
    {
        var payload = request.PayloadAs<IdPayload>();
        var card = _host.ViewModel.Cards.FirstOrDefault(c => c.Id == payload?.Id);
        if (card is null || card.IsBox)
        {
            return IpcResponse.Failure(request.Id, "组件实例不存在。");
        }

        WidgetRepository.Delete(card.Id);
        _host.ViewModel.Cards.Remove(card);
        _host.Window?.SyncCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse SetWidgetVisible(IpcRequest request)
    {
        var payload = request.PayloadAs<VisibilityPayload>();
        if (payload is null)
        {
            return IpcResponse.Failure(request.Id, "缺少参数。");
        }

        var card = _host.ViewModel.Cards.FirstOrDefault(c => c.Id == payload.Id);
        if (card is null)
        {
            return IpcResponse.Failure(request.Id, "组件实例不存在。");
        }

        if (card.IsBox && card.Box is not null)
        {
            BoxRepository.SetVisibility(card.Id, payload.Visible);
        }
        else
        {
            WidgetRepository.SetVisible(card.Id, payload.Visible);
        }

        _host.ViewModel.ReloadAll();
        _host.Window?.SyncCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse ReorderWidgets(IpcRequest request)
    {
        var payload = request.PayloadAs<ReorderPayload>();
        if (payload is null)
        {
            return IpcResponse.Failure(request.Id, "缺少顺序数据。");
        }

        for (var i = 0; i < payload.OrderedIds.Count; i++)
        {
            var card = _host.ViewModel.Cards.FirstOrDefault(c => c.Id == payload.OrderedIds[i]);
            if (card is null)
            {
                continue;
            }

            card.ZOrder = i;
            if (card.IsBox && card.Box is not null)
            {
                BoxRepository.Update(card.Box);
            }
            else
            {
                WidgetRepository.Update(card.Instance);
            }

            _host.Window?.UpdateCardZOrder(card);
        }

        _host.ViewModel.SortCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse UpdateWidgetSettings(IpcRequest request)
    {
        var payload = request.PayloadAs<UpdateWidgetSettingsPayload>();
        if (payload is null)
        {
            return IpcResponse.Failure(request.Id, "缺少设置内容。");
        }

        _host.ViewModel.UpdateWidgetSettings(payload.InstanceId, payload.Settings);
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse CreateBox(IpcRequest request)
    {
        var payload = request.PayloadAs<CreateBoxPayload>() ?? new CreateBoxPayload();
        var type = Enum.TryParse<BoxType>(payload.Type, true, out var parsed) ? parsed : BoxType.Normal;
        var card = _host.ViewModel.AddBox(payload.Name, type, payload.TargetPath);
        return IpcResponse.Success(request.Id, card.Instance);
    }

    private IpcResponse RenameBox(IpcRequest request)
    {
        var payload = request.PayloadAs<RenameBoxPayload>();
        if (payload is null)
        {
            return IpcResponse.Failure(request.Id, "缺少参数。");
        }

        var box = BoxRepository.Get(payload.Id);
        if (box is null)
        {
            return IpcResponse.Failure(request.Id, "收纳盒不存在。");
        }

        BoxService.RenameBox(box, payload.Name);
        var card = _host.ViewModel.Cards.FirstOrDefault(c => c.Id == payload.Id);
        card?.RefreshFromModel();
        _host.Window?.RefreshCardChrome(card);
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse DeleteBox(IpcRequest request)
    {
        var payload = request.PayloadAs<IdPayload>();
        var card = _host.ViewModel.Cards.FirstOrDefault(c => c.Id == payload?.Id);
        var box = payload is null ? null : BoxRepository.Get(payload.Id);
        if (card is null || box is null)
        {
            return IpcResponse.Failure(request.Id, "收纳盒不存在。");
        }

        _host.ViewModel.RemoveCard(card, deleteFiles: true);
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse SetBoxVisible(IpcRequest request)
    {
        var payload = request.PayloadAs<VisibilityPayload>();
        if (payload is null)
        {
            return IpcResponse.Failure(request.Id, "缺少参数。");
        }

        BoxRepository.SetVisibility(payload.Id, payload.Visible);
        _host.ViewModel.ReloadAll();
        _host.Window?.SyncCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse ReorderBoxes(IpcRequest request)
    {
        var payload = request.PayloadAs<ReorderPayload>();
        if (payload is null)
        {
            return IpcResponse.Failure(request.Id, "缺少顺序数据。");
        }

        BoxRepository.Reorder(payload.OrderedIds);
        _host.ViewModel.ReloadAll();
        _host.Window?.SyncCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse TileBoxes(IpcRequest request)
    {
        _host.Window?.TileCards();
        _host.ViewModel.SaveAllPlacements();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse ShowAll(IpcRequest request)
    {
        foreach (var box in BoxRepository.GetAll())
        {
            BoxRepository.SetVisibility(box.Id, true);
        }

        _host.ViewModel.ReloadAll();
        _host.Window?.SyncCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse HideAll(IpcRequest request)
    {
        foreach (var box in BoxRepository.GetAll())
        {
            BoxRepository.SetVisibility(box.Id, false);
        }

        _host.ViewModel.ReloadAll();
        _host.Window?.SyncCards();
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse OpenPath(IpcRequest request, string path)
    {
        Directory.CreateDirectory(path);
        ShellService.Open(path);
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    private IpcResponse ExportDiagnostics(IpcRequest request)
    {
        var path = LogService.ExportDiagnostics();
        return IpcResponse.Success(request.Id, new DiagnosticsResult { Path = path });
    }

    private IpcResponse Shutdown(IpcRequest request)
    {
        Dispatcher.UIThread.Post(_host.Shutdown);
        return IpcResponse.Success(request.Id, new OperationResult { Success = true });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _server?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _acceptLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // ignore
        }

        _cts.Dispose();
    }
}
