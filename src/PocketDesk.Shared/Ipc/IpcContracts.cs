using System.Text.Json;

namespace PocketDesk.Shared.Ipc;

/// <summary>IPC 命令名。请求/响应均为单行 JSON。</summary>
public static class IpcCommands
{
    public const string Ping = "ping";
    public const string GetSnapshot = "settings.get";
    public const string UpdateSettings = "settings.update";

    public const string AddWidget = "widgets.add";
    public const string RemoveWidget = "widgets.remove";
    public const string SetWidgetVisible = "widgets.setVisible";
    public const string ReorderWidgets = "widgets.reorder";
    public const string UpdateWidgetSettings = "widgets.updateSettings";

    public const string CreateBox = "boxes.create";
    public const string RenameBox = "boxes.rename";
    public const string DeleteBox = "boxes.delete";
    public const string SetBoxVisible = "boxes.setVisible";
    public const string ReorderBoxes = "boxes.reorder";
    public const string TileBoxes = "boxes.tile";

    public const string ShowAll = "host.showAll";
    public const string HideAll = "host.hideAll";
    public const string OpenDataDirectory = "host.openDataDir";
    public const string OpenLogDirectory = "host.openLogDir";
    public const string ExportDiagnostics = "host.exportDiagnostics";
    public const string Shutdown = "host.shutdown";
}

/// <summary>一次 IPC 请求。Payload 由命令自行解释。</summary>
public sealed class IpcRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Command { get; set; } = string.Empty;
    public JsonElement? Payload { get; set; }

    public T? PayloadAs<T>() =>
        Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined }
            ? Payload.Value.Deserialize<T>(JsonDefaults.Compact)
            : default;
}

/// <summary>一次 IPC 响应。</summary>
public sealed class IpcResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
    public JsonElement? Payload { get; set; }

    public static IpcResponse Success(string id, object? payload = null) => new()
    {
        Id = id,
        Ok = true,
        Payload = payload is null
            ? null
            : JsonSerializer.SerializeToElement(payload, JsonDefaults.Compact),
    };

    public static IpcResponse Failure(string id, string error) => new()
    {
        Id = id,
        Ok = false,
        Error = error,
    };

    public T? PayloadAs<T>() =>
        Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined }
            ? Payload.Value.Deserialize<T>(JsonDefaults.Compact)
            : default;
}
