using System.ComponentModel;
using System.Runtime.CompilerServices;
using PocketDesk.Shared;

namespace PocketDesk.Core;

/// <summary>收纳盒类型。</summary>
public enum BoxType
{
    /// <summary>文件真实移入应用数据目录。</summary>
    Normal = 0,

    /// <summary>只存绝对路径引用，绝不改动源文件。</summary>
    Mapped = 1,

    /// <summary>绑定现有文件夹，双向同步。</summary>
    Target = 2,
}

/// <summary>收纳项类型。</summary>
public enum ItemKind
{
    File = 0,
    Folder = 1,
    Shortcut = 2,
    Url = 3,
}

/// <summary>排序字段。</summary>
public enum BoxSortField
{
    Name,
    Type,
    ModifiedTime,
    AddedTime,
    Custom,
}

/// <summary>视图模式。</summary>
public enum BoxViewMode
{
    Grid,
    List,
}

/// <summary>文件移动 journal 操作类型。</summary>
public enum JournalOpType
{
    MoveIn = 0,
    MoveOut = 1,
    Restore = 2,
    DeleteBox = 3,
    Rename = 4,
}

/// <summary>journal 状态。</summary>
public enum JournalState
{
    Pending = 0,
    Done = 1,
    RolledBack = 2,
}

/// <summary>同名冲突处理策略。</summary>
public enum ConflictChoice
{
    KeepBoth,
    Replace,
    Skip,
    Cancel,
}

/// <summary>收纳盒外观配置（按盒独立持久化）。</summary>
public sealed class BoxAppearance
{
    public double CornerRadius { get; set; } = 18;
    public double BackgroundOpacity { get; set; } = 0.82;
    public double BorderOpacity { get; set; } = 0.30;
    public double BorderThickness { get; set; } = 1;
    public string BorderColor { get; set; } = "#000000";
    public double IconSize { get; set; } = 48;
    public int Columns { get; set; }
    public int NameLines { get; set; } = 2;
    public bool ShowItemCount { get; set; } = true;
    public string View { get; set; } = "grid";
    public string? AccentColor { get; set; }

    public BoxViewMode ViewMode =>
        string.Equals(View, "list", StringComparison.OrdinalIgnoreCase) ? BoxViewMode.List : BoxViewMode.Grid;

    public BoxAppearance Clone() => (BoxAppearance)MemberwiseClone();

    public static BoxAppearance FromSettings(AppSettings s) => new()
    {
        CornerRadius = s.DefaultCornerRadius,
        BackgroundOpacity = s.CardOpacity,
        BorderOpacity = s.CardBorderOpacity,
        IconSize = s.DefaultIconSize,
        Columns = s.DefaultColumns,
        NameLines = s.DefaultNameLines,
        ShowItemCount = s.ShowItemCount,
        View = "grid",
    };
}

/// <summary>窗口位置与尺寸（宿主窗口内的 DIP 坐标）。</summary>
public sealed class WindowPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 340;
    public string? MonitorDevice { get; set; }
    public double DpiScale { get; set; } = 1.0;
    public string State { get; set; } = "Normal";

    public bool IsRolledUp => string.Equals(State, "RolledUp", StringComparison.OrdinalIgnoreCase);

    public WindowPlacement Clone() => (WindowPlacement)MemberwiseClone();
}

/// <summary>收纳盒实体。</summary>
public sealed class Box
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新收纳盒";
    public BoxType Type { get; set; } = BoxType.Normal;
    public string? TargetPath { get; set; }
    public BoxAppearance Appearance { get; set; } = new();
    public WindowPlacement? Placement { get; set; }
    public int ZOrder { get; set; }
    public bool IsPinned { get; set; }
    public bool IsVisible { get; set; } = true;
    public string SortField { get; set; } = "name";
    public bool SortDescending { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string TypeLabel => Type switch
    {
        BoxType.Normal => "普通收纳盒",
        BoxType.Mapped => "映射收纳盒",
        _ => "目标收纳盒",
    };

    public string TypeGlyph => Type switch
    {
        BoxType.Normal => "\uE8B7",
        BoxType.Mapped => "\uE71B",
        _ => "\uE8DE",
    };

    public string TypeShortLabel => Type switch
    {
        BoxType.Normal => "普通",
        BoxType.Mapped => "映射",
        _ => "目标",
    };
}

/// <summary>收纳项。保持 UI 无关，图标等呈现信息由宿主缓存。</summary>
public sealed class BoxItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private bool _isMissing;
    private string? _missingReason;
    private long _size;
    private DateTime? _modifiedAt;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BoxId { get; set; } = string.Empty;
    public ItemKind Kind { get; set; }
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
            {
                OnPropertyChanged(nameof(TooltipText));
                OnPropertyChanged(nameof(TypeHint));
            }
        }
    }

    public string? SourcePath { get; set; }
    public string? StoredPath { get; set; }
    public string? IconCacheKey { get; set; }
    public double SortIndex { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public string? OriginalBoxId { get; set; }
    public bool IsLink { get; set; }

    public DateTime? ModifiedAt
    {
        get => _modifiedAt;
        set
        {
            if (SetField(ref _modifiedAt, value))
            {
                OnPropertyChanged(nameof(TooltipText));
                OnPropertyChanged(nameof(TypeHint));
            }
        }
    }

    public long Size
    {
        get => _size;
        set
        {
            if (SetField(ref _size, value))
            {
                OnPropertyChanged(nameof(TypeHint));
                OnPropertyChanged(nameof(TooltipText));
            }
        }
    }

    public string? ShortcutTarget { get; set; }

    public string RealPath => StoredPath ?? SourcePath ?? string.Empty;

    public bool HasRealPath => !string.IsNullOrEmpty(RealPath);

    public bool IsReference => string.IsNullOrEmpty(StoredPath);

    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (SetField(ref _isMissing, value))
            {
                OnPropertyChanged(nameof(TooltipText));
            }
        }
    }

    public string? MissingReason
    {
        get => _missingReason;
        set => SetField(ref _missingReason, value);
    }

    public string TooltipText
    {
        get
        {
            var lines = new List<string> { DisplayName };
            if (!string.IsNullOrEmpty(RealPath))
            {
                lines.Add(RealPath);
            }

            if (!string.IsNullOrEmpty(ShortcutTarget))
            {
                lines.Add($"目标：{ShortcutTarget}");
            }

            if (Kind == ItemKind.Folder)
            {
                lines.Add("文件夹");
            }

            if (IsLink)
            {
                lines.Add("链接（仅存引用）");
            }

            if (ModifiedAt is { } modified)
            {
                lines.Add($"修改时间：{modified:yyyy-MM-dd HH:mm}");
            }

            if (IsMissing)
            {
                lines.Add($"不可用：{MissingReason ?? "路径不存在"}");
            }

            if (IsReference && !IsLink && Kind != ItemKind.Url)
            {
                lines.Add("映射引用（不改动源文件）");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string KindGlyph => Kind switch
    {
        ItemKind.Folder => "\uE8B7",
        ItemKind.Shortcut => "\uE71B",
        ItemKind.Url => "\uE774",
        _ => "\uE7C3",
    };

    public string TypeHint
    {
        get
        {
            if (Kind == ItemKind.Url && !string.IsNullOrEmpty(ShortcutTarget))
            {
                return ShortcutTarget!;
            }

            var parent = string.IsNullOrEmpty(RealPath) ? null : Path.GetDirectoryName(RealPath);
            var kind = Kind switch
            {
                ItemKind.Folder => "文件夹",
                ItemKind.Shortcut => "快捷方式",
                ItemKind.Url => "网址",
                _ => Size > 0 ? PathUtil.FormatSize(Size) : "文件",
            };
            return string.IsNullOrEmpty(parent) ? kind : $"{kind} · {parent}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>操作日志条目，用于崩溃恢复。</summary>
public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public JournalOpType OpType { get; set; }
    public string? BoxId { get; set; }
    public string? ItemId { get; set; }
    public string? FromPath { get; set; }
    public string? ToPath { get; set; }
    public JournalState State { get; set; } = JournalState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>批量导入/移动的单项结果。</summary>
public sealed class ImportResult
{
    public required string SourcePath { get; init; }
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string? Error { get; init; }
    public string? ItemName { get; init; }
}

/// <summary>已删除收纳项，供撤销使用。</summary>
public sealed class DeletedItemInfo
{
    public required string BoxId { get; init; }
    public required string DisplayName { get; init; }
    public ItemKind Kind { get; init; }
    public bool WasReference { get; init; }
    public string? OriginalSource { get; init; }
    public string? RestoredPath { get; init; }
    public DateTime DeletedAt { get; init; }
}

/// <summary>界面文案集中定义。</summary>
public static class L
{
    public const string DeleteConfirmTitle = "删除确认";
    public const string Undo = "撤销";
    public const string Deleted = "已删除";
    public const string RestoredToOriginal = "文件已还原到原位置。";
    public const string ReferenceRemoved = "已移除引用，源文件未改动。";
    public const string LinkRestored = "链接已还原。";
    public const string ImportFailed = "导入失败";
    public const string DropHint = "把文件拖到这里";
    public const string EmptyHint = "拖文件到这里，或右键新建";
    public const string CloudRejected = "OneDrive 云占位文件不受支持：请使用映射收纳盒，或先让文件下载到本地。";
    public const string UncRejected = "普通收纳盒不支持网络路径，请改用映射收纳盒。";
    public const string RecycleBinRejected = "请先从回收站还原文件，再拖入收纳盒。";
    public const string ShortcutResolveFailed = "快捷方式已失效";
    public const string PathMissing = "路径不存在";
    public const string NotSupportedCrossBox = "暂不支持把该类型的收纳项移动到目标收纳盒。";
    public const string SameBoxDrop = "收纳项已经在当前收纳盒中。";
}
