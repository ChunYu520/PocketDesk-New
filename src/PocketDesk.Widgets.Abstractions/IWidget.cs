using Avalonia.Controls;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;

namespace PocketDesk.Widgets;

/// <summary>
/// 所有桌面组件的契约。宿主只认识该接口，不引用任何具体组件程序集。
/// 新增天气等组件时，实现本接口并放入宿主 Widgets 目录即可，宿主代码零修改。
/// </summary>
public interface IWidget
{
    /// <summary>稳定的类型 Id，例如 pocketdesk.weather。</summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>Segoe Fluent Icons 字形，用于设置界面与卡片角标。</summary>
    string Glyph { get; }

    /// <summary>是否为宿主内置组件（内置组件不通过插件目录加载）。</summary>
    bool IsBuiltIn { get; }

    /// <summary>是否允许同时创建多个实例。</summary>
    bool SupportsMultipleInstances { get; }

    /// <summary>声明的设置项，设置界面据此动态渲染，不硬编码组件配置页。</summary>
    WidgetSettingsSchema GetSettingsSchema();

    /// <summary>创建卡片内容视图。宿主负责卡片外框、标题、拖拽与焦点指示。</summary>
    Control CreateView(IWidgetContext context);
}

/// <summary>宿主注入给组件的上下文。</summary>
public interface IWidgetContext
{
    WidgetInstance Instance { get; }

    AppSettings GlobalSettings { get; }

    /// <summary>宿主服务容器。组件只通过接口访问数据层，不互相引用。</summary>
    IServiceProvider Services { get; }

    /// <summary>组件通知宿主自身设置已变化（例如天气刷新间隔）。</summary>
    event EventHandler? SettingsChanged;

    string? GetSetting(string key, string? fallback = null);

    /// <summary>写入组件实例设置并持久化。</summary>
    void SetSetting(string key, string? value);

    /// <summary>请求宿主刷新卡片标题/角标等外壳信息。</summary>
    void NotifyMetadataChanged();

    /// <summary>请求宿主临时获取键盘焦点（例如组件内含输入框时）。</summary>
    void RequestKeyboardFocus();
}

/// <summary>组件可选的宿主服务。</summary>
public interface IWidgetHostServices
{
    /// <summary>弹出一条非阻塞提示（托盘气泡或卡片内提示）。</summary>
    void ShowToast(string message);

    /// <summary>打开 WinUI 3 设置界面。</summary>
    void OpenSettings();
}
