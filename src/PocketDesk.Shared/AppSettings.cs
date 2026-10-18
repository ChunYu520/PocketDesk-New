namespace PocketDesk.Shared;

/// <summary>
/// 全局设置。该类型同时被 Avalonia 宿主、WinUI 3 设置界面与 IPC 使用，
/// 因此不依赖任何 UI 框架。
/// </summary>
public sealed class AppSettings
{
    /// <summary>System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>是否跟随系统强调色；关闭时使用 <see cref="AccentColor"/>。</summary>
    public bool FollowSystemAccent { get; set; } = true;

    /// <summary>自定义强调色（#RRGGBB）。</summary>
    public string AccentColor { get; set; } = "#4CC2FF";

    /// <summary>卡片默认圆角（DIP）。</summary>
    public double DefaultCornerRadius { get; set; } = 18;

    /// <summary>卡片背景不透明度（0.2 - 1.0）。</summary>
    public double CardOpacity { get; set; } = 0.82;

    /// <summary>卡片边框不透明度（0 - 1）。</summary>
    public double CardBorderOpacity { get; set; } = 0.30;

    /// <summary>卡片阴影强度（0 - 100）。</summary>
    public double ShadowStrength { get; set; } = 42;

    /// <summary>默认图标尺寸（DIP）。</summary>
    public double DefaultIconSize { get; set; } = 48;

    /// <summary>默认网格列数，0 表示自适应。</summary>
    public int DefaultColumns { get; set; }

    /// <summary>默认文件名显示行数（0 - 3）。</summary>
    public int DefaultNameLines { get; set; } = 2;

    /// <summary>是否在获得键盘焦点的卡片顶部显示强调色指示线。</summary>
    public bool ShowFocusIndicator { get; set; } = true;

    /// <summary>启动时是否恢复上次摆位。</summary>
    public bool RestoreLayoutOnStartup { get; set; } = true;

    /// <summary>是否随系统登录启动。</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>关闭设置窗口/宿主时是否保留托盘图标。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>删除收纳项/收纳盒前是否确认。</summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>是否启用轻量过渡动画。</summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>是否显示标题栏中的收纳项数量。</summary>
    public bool ShowItemCount { get; set; } = true;

    /// <summary>双击收纳项是否直接打开。</summary>
    public bool DoubleClickToOpen { get; set; } = true;

    /// <summary>默认排序字段（name / type / modified / added / custom）。</summary>
    public string DefaultSortField { get; set; } = "name";

    /// <summary>界面语言，仅保留字段以兼容旧数据。</summary>
    public string Language { get; set; } = "zh-CN";

    public bool FirstRunCompleted { get; set; }

    /// <summary>设置界面的窗口位置（WinUI 侧保存，便于下次打开）。</summary>
    public string? SettingsWindowPlacement { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        DefaultCornerRadius = Math.Clamp(DefaultCornerRadius, 0, 32);
        CardOpacity = Math.Clamp(CardOpacity, 0.2, 1.0);
        CardBorderOpacity = Math.Clamp(CardBorderOpacity, 0, 1);
        ShadowStrength = Math.Clamp(ShadowStrength, 0, 100);
        DefaultIconSize = Math.Clamp(DefaultIconSize, 24, 128);
        DefaultColumns = Math.Clamp(DefaultColumns, 0, 12);
        DefaultNameLines = Math.Clamp(DefaultNameLines, 0, 3);
        if (string.IsNullOrWhiteSpace(AccentColor) || !AccentColor.StartsWith('#'))
        {
            AccentColor = "#4CC2FF";
        }

        if (string.IsNullOrWhiteSpace(Theme))
        {
            Theme = "System";
        }

        if (string.IsNullOrWhiteSpace(DefaultSortField))
        {
            DefaultSortField = "name";
        }
    }
}
