using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using Windows.UI;

namespace PocketDesk.Settings.Views;

/// <summary>常规设置：主题、强调色、卡片材质参数与组件层行为。</summary>
public sealed class GeneralPage : UserControl
{
    private readonly MainWindow _owner;
    private readonly AppSettings _settings;
    private bool _loading = true;

    public GeneralPage(MainWindow owner, SettingsSnapshot snapshot)
    {
        _owner = owner;
        _settings = snapshot.Settings.Clone();
        Content = Build();
        _loading = false;
    }

    private FrameworkElement Build()
    {
        var page = new StackPanel { Spacing = 0 };
        page.Children.Add(Ui.PageTitle("常规"));
        page.Children.Add(Ui.PageSubtitle(
            "外观与组件层行为。修改后立即通过命名管道同步给 Avalonia 宿主，不需要重启主进程。"));

        page.Children.Add(Ui.SectionTitle("外观"));
        page.Children.Add(ThemeCard());
        page.Children.Add(AccentCard());
        page.Children.Add(CornerRadiusCard());
        page.Children.Add(SliderCard("卡片不透明度", "半透明卡片是“悬浮于壁纸”的主要视觉线索。", 0.2, 1, 0.02,
            _settings.CardOpacity, value => _settings.CardOpacity = value, v => $"{v:P0}"));
        page.Children.Add(SliderCard("边框不透明度", "边框与背景使用自定义画刷，不随窗口焦点变化。", 0, 1, 0.02,
            _settings.CardBorderOpacity, value => _settings.CardBorderOpacity = value, v => $"{v:P0}"));
        page.Children.Add(SliderCard("阴影强度", "0 为无阴影；阴影仅在卡片外围，不影响点击穿透。", 0, 100, 1,
            _settings.ShadowStrength, value => _settings.ShadowStrength = value, v => $"{v:0}"));
        page.Children.Add(SliderCard("默认图标尺寸", "新建收纳盒使用的图标边长。", 24, 128, 4,
            _settings.DefaultIconSize, value => _settings.DefaultIconSize = value, v => $"{v:0} px"));

        page.Children.Add(Ui.SectionTitle("组件层行为"));
        page.Children.Add(ToggleRow("显示键盘输入指示线", "仅在获得真实键盘焦点的卡片顶部显示强调色细线。",
            _settings.ShowFocusIndicator, value => _settings.ShowFocusIndicator = value));
        page.Children.Add(ToggleRow("显示收纳项数量", "在卡片标题下方显示项目数。",
            _settings.ShowItemCount, value => _settings.ShowItemCount = value));
        page.Children.Add(ToggleRow("启动时恢复上次布局", "恢复每张卡片的位置、尺寸与层级。",
            _settings.RestoreLayoutOnStartup, value => _settings.RestoreLayoutOnStartup = value));
        page.Children.Add(ToggleRow("双击打开收纳项", "关闭后双击仅选中，不直接打开文件。",
            _settings.DoubleClickToOpen, value => _settings.DoubleClickToOpen = value));
        page.Children.Add(ToggleRow("删除前确认", "删除收纳盒、收纳项或组件前弹出确认。",
            _settings.ConfirmDelete, value => _settings.ConfirmDelete = value));
        page.Children.Add(ToggleRow("随 Windows 登录启动", "写入当前用户的启动项，不申请管理员权限。",
            _settings.LaunchAtStartup, value =>
            {
                _settings.LaunchAtStartup = value;
                ApplyAutostart(value, _owner.Snapshot?.HostExePath);
            }));
        page.Children.Add(ToggleRow("关闭时保留托盘图标", "关闭设置窗口不影响宿主；宿主退出由托盘菜单控制。",
            _settings.CloseToTray, value => _settings.CloseToTray = value));

        var reset = Ui.Button("恢复默认外观", async (_, _) =>
        {
            var defaults = new AppSettings();
            _settings.Theme = defaults.Theme;
            _settings.FollowSystemAccent = defaults.FollowSystemAccent;
            _settings.AccentColor = defaults.AccentColor;
            _settings.DefaultCornerRadius = defaults.DefaultCornerRadius;
            _settings.CardOpacity = defaults.CardOpacity;
            _settings.CardBorderOpacity = defaults.CardBorderOpacity;
            _settings.ShadowStrength = defaults.ShadowStrength;
            _settings.DefaultIconSize = defaults.DefaultIconSize;
            QueueSave();
            await _owner.RefreshAsync();
        }, "\uE777");
        reset.Margin = new Thickness(0, 18, 0, 0);
        page.Children.Add(reset);
        page.Children.Add(new TextBlock
        {
            Text = "提示：设置保存在宿主的 SQLite 数据库中，卸载应用不会删除这些配置。",
            FontSize = 12,
            Foreground = Ui.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });

        return page;
    }

    private Border ThemeCard()
    {
        var combo = Ui.Combo(("System", "跟随系统"), ("Light", "浅色"), ("Dark", "深色"));
        Ui.SelectCombo(combo, _settings.Theme);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.Theme = Ui.ComboValue(combo) ?? "System";
            QueueSave();
        };
        return Ui.SettingRow("主题", "跟随系统会响应 Windows 的浅色/深色切换。", combo);
    }

    private Border AccentCard()
    {
        var follow = Ui.Toggle(_settings.FollowSystemAccent, "开", "关");
        follow.Toggled += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.FollowSystemAccent = follow.IsOn;
            QueueSave();
        };

        var hex = Ui.TextInput(_settings.AccentColor, "#4CC2FF");
        hex.Width = 150;
        var preview = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Ui.ParseColor(_settings.AccentColor, Color.FromArgb(255, 76, 194, 255))),
        };
        hex.TextChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.AccentColor = hex.Text.Trim();
            preview.Background = new SolidColorBrush(Ui.ParseColor(_settings.AccentColor,
                Color.FromArgb(255, 76, 194, 255)));
            QueueSave();
        };

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        colorRow.Children.Add(hex);
        colorRow.Children.Add(preview);

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var value in new[] { "#4CC2FF", "#7C6BFF", "#FF6B9A", "#FFB454", "#36C98F", "#FF6B6B" })
        {
            var color = Ui.ParseColor(value, Color.FromArgb(255, 76, 194, 255));
            var swatch = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = Ui.CardStroke,
            };
            swatch.Click += (_, _) =>
            {
                hex.Text = value;
                _settings.AccentColor = value;
                preview.Background = new SolidColorBrush(color);
                QueueSave();
            };
            presets.Children.Add(swatch);
        }

        var custom = new StackPanel { Spacing = 4 };
        custom.Children.Add(colorRow);
        custom.Children.Add(presets);

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Ui.SettingRow("跟随系统强调色", "使用 Windows 个性化中的强调色。", follow, 0));
        panel.Children.Add(Ui.SettingRow("自定义强调色", "关闭“跟随系统”后生效。", custom, 0));
        return Ui.Card(panel, 8);
    }

    private Border CornerRadiusCard()
    {
        var value = new TextBlock { Text = $"{_settings.DefaultCornerRadius:0} px" };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 32,
            StepFrequency = 1,
            Value = _settings.DefaultCornerRadius,
            Width = 360,
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = $"{e.NewValue:0} px";
            if (_loading)
            {
                return;
            }

            _settings.DefaultCornerRadius = e.NewValue;
            QueueSave();
        };
        return Ui.Card(Ui.SliderRow("默认圆角", "影响新建收纳盒；已有收纳盒可在卡片菜单中单独调整。", slider, value), 8);
    }

    private Border SliderCard(
        string title,
        string description,
        double min,
        double max,
        double step,
        double current,
        Action<double> apply,
        Func<double, string> format)
    {
        var value = new TextBlock { Text = format(current) };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = current,
            Width = 360,
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = format(e.NewValue);
            if (_loading)
            {
                return;
            }

            apply(e.NewValue);
            QueueSave();
        };
        return Ui.Card(Ui.SliderRow(title, description, slider, value), 8);
    }

    private Border ToggleRow(string title, string description, bool value, Action<bool> apply)
    {
        var toggle = Ui.Toggle(value);
        toggle.Toggled += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            apply(toggle.IsOn);
            QueueSave();
        };
        return Ui.SettingRow(title, description, toggle);
    }

    private void QueueSave()
    {
        _settings.Normalize();
        _owner.QueueSettingsSave(_settings);
    }

    private static void ApplyAutostart(bool enabled, string? hostExePath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var candidates = new[]
                {
                    hostExePath,
                    Path.Combine(AppContext.BaseDirectory, "PocketDesk.Host.exe"),
                    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "PocketDesk.Host.exe")),
                };
                var exe = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
                if (exe is not null)
                {
                    key.SetValue("PocketDesk-New", $"\"{exe}\"");
                }
            }
            else
            {
                key.DeleteValue("PocketDesk-New", throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表权限失败不阻断设置保存。
        }
    }
}
