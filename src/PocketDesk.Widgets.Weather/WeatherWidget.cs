using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;

namespace PocketDesk.Widgets.Weather;

/// <summary>
/// 占位天气组件：不调用真实天气 API，只证明宿主可以在不修改代码的情况下加载新组件，
/// 并让 WinUI 3 设置界面根据 Schema 动态渲染“城市名称 / 刷新间隔 / 温度单位”等配置项。
/// </summary>
public sealed class WeatherWidget : WidgetBase
{
    public override string Id => AppInfo.WeatherWidgetId;

    public override string DisplayName => "天气";

    public override string Description => "占位天气组件，用于验证组件扩展路径（不需要真实天气 API）。";

    public override string Glyph => "\uE706";

    public override bool IsBuiltIn => false;

    public override bool SupportsMultipleInstances => true;

    public override WidgetSettingsSchema GetSettingsSchema() =>
        new WidgetSchemaBuilder(
                AppInfo.WeatherWidgetId,
                "天气组件设置",
                "这些字段由组件自身声明，PocketDesk 设置界面根据 Schema 动态渲染，不含任何天气专用代码。")
            .Text("city", "城市名称", "上海", "显示在天气卡片标题下方。", "例如：上海")
            .Number("refreshMinutes", "刷新间隔（分钟）", 30, 5, 240, 5, "占位组件不会真正请求网络，仅保存配置。")
            .Choice("units", "温度单位", "c", "选择摄氏度或华氏度。", ("c", "摄氏度 °C"), ("f", "华氏度 °F"))
            .Toggle("showFeelsLike", "显示体感温度", true, "在温度下方显示体感温度。")
            .Color("accent", "强调色", "#4CC2FF", "天气卡片的主色。")
            .Build();

    protected override Control BuildView(IWidgetContext context)
    {
        var accent = Color.Parse(context.GetSetting("accent", "#4CC2FF") ?? "#4CC2FF");
        var city = context.GetSetting("city", "上海") ?? "上海";
        var units = context.GetSetting("units", "c") ?? "c";
        var showFeels = string.Equals(context.GetSetting("showFeelsLike", "true"), "true", StringComparison.OrdinalIgnoreCase);
        var refresh = context.GetSetting("refreshMinutes", "30") ?? "30";

        var root = new Border
        {
            CornerRadius = new CornerRadius(0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(accent, 0),
                    new GradientStop(Color.FromArgb(0xCC, 0x0B, 0x1B, 0x33), 1),
                },
            },
            Padding = new Thickness(18, 14, 18, 16),
            Child = BuildContent(context, city, units, showFeels, refresh, accent),
        };

        context.SettingsChanged += (_, _) =>
        {
            // 设置界面修改后即时重建内容，验证 Schema 驱动配置的闭环。
            var newCity = context.GetSetting("city", "上海") ?? "上海";
            var newUnits = context.GetSetting("units", "c") ?? "c";
            var newShowFeels = string.Equals(context.GetSetting("showFeelsLike", "true"), "true", StringComparison.OrdinalIgnoreCase);
            var newRefresh = context.GetSetting("refreshMinutes", "30") ?? "30";
            var newAccent = Color.Parse(context.GetSetting("accent", "#4CC2FF") ?? "#4CC2FF");
            root.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(newAccent, 0),
                    new GradientStop(Color.FromArgb(0xCC, 0x0B, 0x1B, 0x33), 1),
                },
            };
            root.Child = BuildContent(context, newCity, newUnits, newShowFeels, newRefresh, newAccent);
        };

        return root;
    }

    private static Control BuildContent(
        IWidgetContext context,
        string city,
        string units,
        bool showFeels,
        string refresh,
        Color accent)
    {
        var isCelsius = !string.Equals(units, "f", StringComparison.OrdinalIgnoreCase);
        var temperature = isCelsius ? "23°" : "73°";
        var feels = isCelsius ? "体感 25°C" : "体感 77°F";

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = city,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "局部多云 · 占位数据",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White, 0.72),
        });
        panel.Children.Add(new TextBlock
        {
            Text = temperature,
            FontSize = 56,
            FontWeight = FontWeight.Light,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 2, 0, 0),
        });
        if (showFeels)
        {
            panel.Children.Add(new TextBlock
            {
                Text = feels,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.White, 0.78),
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"每 {refresh} 分钟刷新 · 由组件 Schema 配置",
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White, 0.58),
            Margin = new Thickness(0, 8, 0, 0),
        });

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var updated = new TextBlock
        {
            Text = $"更新于 {DateTime.Now:HH:mm}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White, 0.55),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(updated);

        var refreshButton = new Button
        {
            Content = "刷新",
            FontSize = 11,
            Padding = new Thickness(10, 4),
            Background = new SolidColorBrush(Colors.White, 0.16),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        refreshButton.Click += (_, _) =>
        {
            updated.Text = $"更新于 {DateTime.Now:HH:mm}";
            context.RequestKeyboardFocus();
        };
        Grid.SetColumn(refreshButton, 1);
        footer.Children.Add(refreshButton);
        panel.Children.Add(footer);
        return panel;
    }
}
