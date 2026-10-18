using Avalonia.Controls;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets;

namespace PocketDesk.Host.Widgets;

/// <summary>内置的第一个组件：收纳盒。它与其他组件实现同一契约，由宿主统一布局与生命周期管理。</summary>
public sealed class BoxWidget : IWidget
{
    public string Id => AppInfo.BoxWidgetId;

    public string DisplayName => "收纳盒";

    public string Description => "普通 / 映射 / 目标三种收纳盒，支持拖入拖出与文件安全移动。";

    public string Glyph => "\uE8B7";

    public bool IsBuiltIn => true;

    public bool SupportsMultipleInstances => true;

    public WidgetSettingsSchema GetSettingsSchema() =>
        new WidgetSchemaBuilder(AppInfo.BoxWidgetId, "收纳盒设置", "每个收纳盒的外观与行为；修改后立即生效。")
            .Choice("view", "视图", "grid", "图标网格或详细列表。", ("grid", "网格"), ("list", "列表"))
            .Toggle("showItemCount", "显示收纳项数量", true, "在标题下方显示项目数。")
            .Choice("sortField", "排序方式", "name", "收纳项在卡片内的排列顺序。",
                ("name", "名称"), ("type", "类型"), ("modified", "修改时间"), ("added", "添加时间"), ("custom", "自定义"))
            .Build();

    public Control CreateView(IWidgetContext context)
    {
        var host = (Services.HostServices?)context.Services.GetService(typeof(Services.HostServices))
                   ?? throw new InvalidOperationException("宿主服务不可用。");
        var box = Core.BoxService.GetBox(context.Instance.Id)
                  ?? throw new InvalidOperationException("收纳盒不存在或已被删除。");
        return new Views.BoxCardContent(box, context, host);
    }
}
