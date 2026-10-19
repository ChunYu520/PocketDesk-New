using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PocketDesk.Settings;

/// <summary>设置界面共用的轻量 UI 构件，保持 WinUI 3 原生观感。</summary>
public static class Ui
{
    public static bool IsDark => Application.Current.RequestedTheme == ApplicationTheme.Dark;

    public static Brush Brush(string key, Color fallback)
    {
        try
        {
            if (Application.Current.Resources[key] is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
            // 资源不存在。
        }

        return new SolidColorBrush(fallback);
    }

    public static Brush CardBackground => Brush("CardBackgroundFillColorDefaultBrush",
        IsDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 255, 255, 255));

    public static Brush CardStroke => Brush("CardStrokeColorDefaultBrush",
        IsDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0));

    public static Brush TextPrimary => Brush("TextFillColorPrimaryBrush",
        IsDark ? Colors.White : Color.FromArgb(255, 28, 28, 28));

    public static Brush TextSecondary => Brush("TextFillColorSecondaryBrush",
        IsDark ? Color.FromArgb(180, 255, 255, 255) : Color.FromArgb(150, 0, 0, 0));

    public static Brush Accent => Brush("AccentFillColorDefaultBrush", Color.FromArgb(255, 0, 120, 212));

    public static Brush Danger => Brush("SystemFillColorCriticalBrush", Color.FromArgb(255, 196, 43, 28));

    public static Border Card(UIElement child, double padding = 20) => new()
    {
        Background = CardBackground,
        BorderBrush = CardStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(padding),
        Child = child,
    };

    /// <summary>Win11 风格图标底座：强调色淡底 + 居中图标，避免 FontIcon 基线造成的视觉偏移。</summary>
    public static Grid IconTile(string glyph, double size = 40, double glyphSize = 20, Brush? foreground = null)
    {
        var color = foreground ?? Accent;
        var tile = new Grid
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        tile.Children.Add(new Border
        {
            Background = color,
            Opacity = 0.14,
            CornerRadius = new CornerRadius(size * 0.22),
        });
        tile.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = glyphSize,
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });
        return tile;
    }

    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 28,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary,
        Margin = new Thickness(0, 0, 0, 4),
    };

    public static TextBlock PageSubtitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = TextSecondary,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 20),
    };

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary,
        Margin = new Thickness(0, 18, 0, 8),
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = TextSecondary,
        TextWrapping = TextWrapping.Wrap,
    };

    public static Border SettingRow(string title, string? description, FrameworkElement control, double bottomMargin = 8)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
            ColumnSpacing = 24,
        };
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, bottomMargin),
            Child = grid,
        };
    }

    public static Border SliderRow(string title, string description, Slider slider, TextBlock valueText)
    {
        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            Foreground = TextPrimary,
        });
        valueText.FontSize = 12;
        valueText.Foreground = TextSecondary;
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        slider.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(slider);

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(6),
            Child = panel,
        };
    }

    public static Button PrimaryButton(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Style = TryStyle("AccentButtonStyle"),
            Padding = new Thickness(16, 8, 16, 8),
        };
        button.Click += click;
        return button;
    }

    public static Button Button(string text, RoutedEventHandler click, string? icon = null)
    {
        var button = new Button
        {
            Padding = new Thickness(12, 7, 12, 7),
        };
        if (icon is not null)
        {
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = icon, FontSize = 14 },
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                },
            };
        }
        else
        {
            button.Content = text;
        }

        button.Click += click;
        return button;
    }

    public static Style? TryStyle(string key)
    {
        try
        {
            return Application.Current.Resources[key] as Style;
        }
        catch
        {
            return null;
        }
    }

    public static FontIcon Icon(string glyph, double size = 18) => new()
    {
        Glyph = glyph,
        FontSize = size,
        Foreground = TextSecondary,
    };

    public static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var value = text.Trim().TrimStart('#');
        try
        {
            if (value.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(value[..2], 16),
                    Convert.ToByte(value.Substring(2, 2), 16),
                    Convert.ToByte(value.Substring(4, 2), 16),
                    Convert.ToByte(value.Substring(6, 2), 16));
            }

            if (value.Length == 6)
            {
                return Color.FromArgb(
                    255,
                    Convert.ToByte(value[..2], 16),
                    Convert.ToByte(value.Substring(2, 2), 16),
                    Convert.ToByte(value.Substring(4, 2), 16));
            }
        }
        catch
        {
            // fall through
        }

        return fallback;
    }

    public static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color Contrast(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.62 ? Color.FromArgb(255, 20, 20, 20) : Colors.White;
    }

    public static ToggleSwitch Toggle(bool isOn, string? onContent = null, string? offContent = null) => new()
    {
        IsOn = isOn,
        OnContent = onContent,
        OffContent = offContent,
        MinWidth = 0,
    };

    public static ComboBox Combo(params (string Value, string Label)[] items)
    {
        var combo = new ComboBox { MinWidth = 200 };
        foreach (var (value, label) in items)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }

        return combo;
    }

    public static void SelectCombo(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    public static string? ComboValue(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    public static TextBox TextInput(string text, string? placeholder = null) => new()
    {
        Text = text,
        PlaceholderText = placeholder,
        MinWidth = 240,
        Padding = new Thickness(10, 6, 10, 6),
    };
}
