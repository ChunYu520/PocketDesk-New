using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;

namespace PocketDesk.Settings.Services;

public sealed class HostUnavailableException : Exception
{
    public HostUnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// 设置界面到宿主的命名管道客户端。每次请求使用独立连接，避免长连接状态泄漏；
/// 连接超时短，宿主未启动时快速降级为只读提示。
/// </summary>
public sealed class SettingsClient : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await SendAsync<SettingsSnapshot>(IpcCommands.GetSnapshot, null, cancellationToken)
            .ConfigureAwait(false);
        return snapshot ?? throw new HostUnavailableException("宿主返回了空的状态快照。");
    }

    public async Task SendCommandAsync(string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        await SendAsync<OperationResult>(command, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> SendAsync<T>(string command, object? payload = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));

            await using var pipe = new NamedPipeClientStream(
                ".",
                AppInfo.HostPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(1500, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HostUnavailableException("无法连接 PocketDesk 宿主（连接超时）。请先启动桌面组件层。");
            }
            catch (IOException ex)
            {
                throw new HostUnavailableException("无法连接 PocketDesk 宿主。请先启动桌面组件层。", ex);
            }

            using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            var request = new IpcRequest
            {
                Command = command,
                Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonDefaults.Compact),
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Compact)).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null)
            {
                throw new HostUnavailableException("宿主已关闭连接。");
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(line, JsonDefaults.Compact)
                           ?? throw new HostUnavailableException("宿主返回了无法解析的响应。");
            if (!response.Ok)
            {
                throw new InvalidOperationException(response.Error ?? "宿主操作失败。");
            }

            return response.PayloadAs<T>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HostUnavailableException("宿主响应超时。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
