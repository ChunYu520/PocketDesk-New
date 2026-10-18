using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI");

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
        var root = new Border
        {
            Padding = new Thickness(18, 14, 18, 16),
            ClipToBounds = true,
            Background = Gradient(accent),
            Child = BuildContent(context, accent),
        };

        context.SettingsChanged += (_, _) =>
        {
            var newAccent = Color.Parse(context.GetSetting("accent", "#4CC2FF") ?? "#4CC2FF");
            root.Background = Gradient(newAccent);
            root.Child = BuildContent(context, newAccent);
        };

        return root;
    }

    private static LinearGradientBrush Gradient(Color accent) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(accent, 0),
            new GradientStop(Color.FromArgb(0xF2, 0x11, 0x2A, 0x42), 0.62),
            new GradientStop(Color.FromArgb(0xFF, 0x0C, 0x18, 0x26), 1),
        },
    };

    private static Control BuildContent(IWidgetContext context, Color accent)
    {
        var city = context.GetSetting("city", "上海") ?? "上海";
        var units = context.GetSetting("units", "c") ?? "c";
        var showFeels = string.Equals(context.GetSetting("showFeelsLike", "true"), "true", StringComparison.OrdinalIgnoreCase);
        var refresh = context.GetSetting("refreshMinutes", "30") ?? "30";
        var isCelsius = !string.Equals(units, "f", StringComparison.OrdinalIgnoreCase);
        var temperature = isCelsius ? "23°" : "73°";
        var feels = isCelsius ? "体感 25°" : "体感 77°";

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var cityBlock = new StackPanel { Spacing = 2 };
        cityBlock.Children.Add(new TextBlock
        {
            Text = city,
            FontFamily = UiFont,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
        });
        cityBlock.Children.Add(new TextBlock
        {
            Text = "局部多云 · 占位数据",
            FontFamily = UiFont,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White, 0.66),
        });
        top.Children.Add(cityBlock);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3),
            Background = new SolidColorBrush(Colors.White, 0.16),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "占位",
                FontFamily = UiFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.White, 0.9),
            },
        };
        Grid.SetColumn(badge, 1);
        top.Children.Add(badge);
        root.Children.Add(top);

        var middle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        middle.Children.Add(new TextBlock
        {
            Text = temperature,
            FontFamily = UiFont,
            FontSize = 48,
            FontWeight = FontWeight.Light,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var condition = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "空气良",
                    FontFamily = UiFont,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White, 0.72),
                },
                new TextBlock
                {
                    Text = showFeels ? feels : "—",
                    FontFamily = UiFont,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White, 0.72),
                },
            },
        };
        middle.Children.Add(condition);
        Grid.SetRow(middle, 1);
        root.Children.Add(middle);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        var updated = new TextBlock
        {
            Text = $"更新于 {DateTime.Now:HH:mm} · 每 {refresh} 分钟",
            FontFamily = UiFont,
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.White, 0.55),
            VerticalAlignment = VerticalAlignment.Center,
        };
        footer.Children.Add(updated);

        var refreshButton = new Button
        {
            Content = "刷新",
            FontFamily = UiFont,
            FontSize = 10.5,
            Padding = new Thickness(10, 4),
            Background = new SolidColorBrush(Colors.White, 0.16),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        refreshButton.PointerEntered += (_, _) =>
            refreshButton.Background = new SolidColorBrush(Colors.White, 0.26);
        refreshButton.PointerExited += (_, _) =>
            refreshButton.Background = new SolidColorBrush(Colors.White, 0.16);
        refreshButton.Click += (_, _) => updated.Text = $"更新于 {DateTime.Now:HH:mm} · 每 {refresh} 分钟";
        Grid.SetColumn(refreshButton, 1);
        footer.Children.Add(refreshButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }
}
