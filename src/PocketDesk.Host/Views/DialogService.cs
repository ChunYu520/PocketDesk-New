using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PocketDesk.Core;
using PocketDesk.Host.Services;

namespace PocketDesk.Host.Views;

public sealed record NewBoxRequest(string Name, BoxType Type, string? TargetPath);

/// <summary>宿主的轻量对话框。全部使用 Avalonia 自绘，避免 WinUI/系统弹窗打断组件层的视觉连续性。</summary>
public static class DialogService
{
    private static ThemePalette Palette => HostServices.Current?.Theme.Palette ?? new ThemePalette();

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmText = "删除",
        bool danger = true)
    {
        var result = false;
        var window = CreateDialog(owner, title, 420);
        var body = new StackPanel { Spacing = 18 };
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(Palette.TextSecondary),
            FontSize = 13,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = CreateButton("取消", false);
        cancel.Click += (_, _) => window.Close();
        var ok = CreateButton(confirmText, true, danger ? Palette.Danger : Palette.Accent);
        ok.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        window.Content = DialogShell(body);
        await window.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string message, string initialValue = "")
    {
        string? result = null;
        var window = CreateDialog(owner, title, 440);
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(Palette.TextSecondary),
            FontSize = 13,
        });

        var input = new TextBox
        {
            Text = initialValue,
            Background = Brush(Palette.InputBackground),
            Foreground = Brush(Palette.TextPrimary),
            BorderBrush = Brush(Palette.CardBorder),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
        };
        body.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = CreateButton("取消", false);
        cancel.Click += (_, _) => window.Close();
        var ok = CreateButton("确定", true, Palette.Accent);
        void Commit()
        {
            result = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
            window.Close();
        }

        ok.Click += (_, _) => Commit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                window.Close();
                e.Handled = true;
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        window.Content = DialogShell(body);
        window.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        await window.ShowDialog(owner);
        return result;
    }

    public static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        var window = CreateDialog(owner, title, 460);
        var body = new StackPanel { Spacing = 18 };
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(Palette.TextSecondary),
            FontSize = 13,
        });
        var ok = CreateButton("知道了", true, Palette.Accent);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Click += (_, _) => window.Close();
        body.Children.Add(ok);
        window.Content = DialogShell(body);
        await window.ShowDialog(owner);
    }

    public static async Task<ConflictChoice> ConflictAsync(Window owner, string fileName, string destination)
    {
        var choice = ConflictChoice.Cancel;
        var window = CreateDialog(owner, "同名文件冲突", 520);
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(new TextBlock
        {
            Text = $"收纳盒中已存在「{fileName}」。\n{destination}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(Palette.TextSecondary),
            FontSize = 13,
        });

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string text, ConflictChoice value, bool primary)
        {
            var button = CreateButton(text, primary, Palette.Accent);
            button.Margin = new Thickness(8, 0, 0, 0);
            button.Click += (_, _) =>
            {
                choice = value;
                window.Close();
            };
            buttons.Children.Add(button);
        }

        Add("保留两者", ConflictChoice.KeepBoth, true);
        Add("替换", ConflictChoice.Replace, false);
        Add("跳过", ConflictChoice.Skip, false);
        Add("取消", ConflictChoice.Cancel, false);
        body.Children.Add(buttons);
        window.Content = DialogShell(body);
        await window.ShowDialog(owner);
        return choice;
    }

    public static async Task<bool> LargeFileAsync(Window owner, string fileName, long size)
    {
        return await ConfirmAsync(
            owner,
            "收纳大文件",
            $"「{fileName}」大小为 {PathUtil.FormatSize(size)}，收纳会把它移动到 PocketDesk 数据目录。是否继续？",
            "继续收纳",
            danger: false);
    }

    public static async Task<NewBoxRequest?> NewBoxAsync(Window owner)
    {
        NewBoxRequest? result = null;
        var window = CreateDialog(owner, "新建收纳盒", 520);
        var body = new StackPanel { Spacing = 14 };

        body.Children.Add(Label("名称"));
        var name = new TextBox
        {
            Text = "收纳盒",
            Watermark = "例如：工作资料",
            Background = Brush(Palette.InputBackground),
            Foreground = Brush(Palette.TextPrimary),
            BorderBrush = Brush(Palette.CardBorder),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
        };
        body.Children.Add(name);

        body.Children.Add(Label("类型"));
        var type = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brush(Palette.InputBackground),
            Foreground = Brush(Palette.TextPrimary),
            CornerRadius = new CornerRadius(8),
        };
        type.Items.Add(new ComboBoxItem { Content = "普通收纳盒 · 文件真实移入数据目录", Tag = BoxType.Normal });
        type.Items.Add(new ComboBoxItem { Content = "映射收纳盒 · 只保存路径引用", Tag = BoxType.Mapped });
        type.Items.Add(new ComboBoxItem { Content = "目标收纳盒 · 绑定现有文件夹", Tag = BoxType.Target });
        type.SelectedIndex = 0;
        body.Children.Add(type);

        var targetLabel = Label("目标文件夹");
        var targetPanel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var target = new TextBox
        {
            Watermark = "选择已有文件夹",
            IsReadOnly = true,
            Background = Brush(Palette.InputBackground),
            Foreground = Brush(Palette.TextPrimary),
            BorderBrush = Brush(Palette.CardBorder),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
        };
        var browse = CreateButton("浏览…", false);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Click += async (_, _) =>
        {
            try
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "选择目标文件夹",
                    AllowMultiple = false,
                });
                var path = folders.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    target.Text = path;
                }
            }
            catch
            {
                // 用户在系统对话框取消。
            }
        };
        targetPanel.Children.Add(target);
        Grid.SetColumn(browse, 1);
        targetPanel.Children.Add(browse);
        var targetBox = new StackPanel { Spacing = 6, IsVisible = false, Children = { targetLabel, targetPanel } };
        body.Children.Add(targetBox);

        type.SelectionChanged += (_, _) =>
        {
            targetBox.IsVisible = (type.SelectedItem as ComboBoxItem)?.Tag is BoxType.Target;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var cancel = CreateButton("取消", false);
        cancel.Click += (_, _) => window.Close();
        var create = CreateButton("创建", true, Palette.Accent);
        create.Click += (_, _) =>
        {
            var selectedType = (type.SelectedItem as ComboBoxItem)?.Tag is BoxType t ? t : BoxType.Normal;
            if (selectedType == BoxType.Target && !Directory.Exists(target.Text))
            {
                target.BorderBrush = Brush(Palette.Danger);
                return;
            }

            result = new NewBoxRequest(
                string.IsNullOrWhiteSpace(name.Text) ? "收纳盒" : name.Text.Trim(),
                selectedType,
                selectedType == BoxType.Target ? target.Text : null);
            window.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        body.Children.Add(buttons);

        window.Content = DialogShell(body);
        await window.ShowDialog(owner);
        return result;
    }

    private static Window CreateDialog(Window owner, string title, double width)
    {
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush(Palette.MenuBackground),
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None },
        };
    }

    private static Control DialogShell(Control content)
    {
        return new Border
        {
            Background = Brush(Palette.MenuBackground),
            BorderBrush = Brush(Palette.MenuBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            Child = content,
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush(Palette.TextSecondary),
    };

    private static Button CreateButton(string text, bool primary, Color? accent = null)
    {
        var color = accent ?? Palette.Accent;
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(18, 8),
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Background = primary ? Brush(color) : Brush(Palette.Hover),
            Foreground = primary ? Brush(ContrastColor(color)) : Brush(Palette.TextPrimary),
            BorderThickness = new Thickness(0),
        };
        return button;
    }

    private static Color ContrastColor(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.6 ? Color.Parse("#111318") : Colors.White;
    }

    private static IBrush Brush(Color color) => new SolidColorBrush(color);
}
