using PocketDesk.Shared.Widgets;

namespace PocketDesk.Shared.Ipc;

/// <summary>设置界面读取的完整状态快照。</summary>
public sealed class SettingsSnapshot
{
    public AppSettings Settings { get; set; } = new();
    public List<WidgetTypeInfo> WidgetTypes { get; set; } = new();
    public List<WidgetInstance> Widgets { get; set; } = new();
    public List<BoxSummary> Boxes { get; set; } = new();
    public string HostVersion { get; set; } = AppInfo.Version;
    public string HostExePath { get; set; } = string.Empty;
    public string DataRoot { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = string.Empty;
    public DateTime HostStartedAt { get; set; }
    public string? LastRecoveryMessage { get; set; }
}

/// <summary>收纳盒摘要，供设置界面管理，不包含文件内容。</summary>
public sealed class BoxSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Normal";
    public string? TargetPath { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsPinned { get; set; }
    public int ZOrder { get; set; }
    public int ItemCount { get; set; }
    public string View { get; set; } = "grid";
    public DateTime CreatedAt { get; set; }
}

/// <summary>更新全局设置的请求负载，未提供的字段保持原值。</summary>
public sealed class UpdateSettingsPayload
{
    public AppSettings? Settings { get; set; }
}

/// <summary>添加组件实例。</summary>
public sealed class AddWidgetPayload
{
    public string TypeId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

/// <summary>更新单个组件实例的设置。</summary>
public sealed class UpdateWidgetSettingsPayload
{
    public string InstanceId { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new();
}

/// <summary>创建收纳盒。</summary>
public sealed class CreateBoxPayload
{
    public string? Name { get; set; }
    public string Type { get; set; } = "Normal";
    public string? TargetPath { get; set; }
}

/// <summary>重命名收纳盒。</summary>
public sealed class RenameBoxPayload
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>按 Id 操作单个对象。</summary>
public sealed class IdPayload
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>调整顺序。</summary>
public sealed class ReorderPayload
{
    public List<string> OrderedIds { get; set; } = new();
}

/// <summary>显示/隐藏。</summary>
public sealed class VisibilityPayload
{
    public string Id { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
}

/// <summary>导出诊断日志的结果。</summary>
public sealed class DiagnosticsResult
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>通用操作结果。</summary>
public sealed class OperationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
