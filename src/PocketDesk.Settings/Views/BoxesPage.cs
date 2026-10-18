using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using Windows.Storage.Pickers;
using Windows.UI;

namespace PocketDesk.Settings.Views;

/// <summary>收纳盒管理：创建三种模式、重命名、显示隐藏、排序、删除与网格排列。</summary>
public sealed class BoxesPage : UserControl
{
    private readonly MainWindow _owner;
    private readonly SettingsSnapshot _snapshot;

    public BoxesPage(MainWindow owner, SettingsSnapshot snapshot)
    {
        _owner = owner;
        _snapshot = snapshot;
        Content = Build();
    }

    private FrameworkElement Build()
    {
        var page = new StackPanel { Spacing = 0 };
        page.Children.Add(Ui.PageTitle("收纳盒"));
        page.Children.Add(Ui.PageSubtitle(
            "收纳盒是第一个内置 Widget。普通盒会真实移动文件并写入 journal；映射盒只保存路径引用；目标盒绑定现有文件夹。"));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 14) };
        actions.Children.Add(Ui.PrimaryButton("新建收纳盒", async (_, _) => await CreateBoxAsync()));
        actions.Children.Add(Ui.Button("网格排列", async (_, _) => await RunCommandAsync(IpcCommands.TileBoxes, null, "已排列收纳盒"), "\uE80A"));
        actions.Children.Add(Ui.Button("显示全部", async (_, _) => await RunCommandAsync(IpcCommands.ShowAll, null, "已显示全部收纳盒"), "\uE7B3"));
        actions.Children.Add(Ui.Button("隐藏全部", async (_, _) => await RunCommandAsync(IpcCommands.HideAll, null, "已隐藏全部收纳盒"), "\uE7B4"));
        page.Children.Add(actions);

        if (_snapshot.Boxes.Count == 0)
        {
            page.Children.Add(Ui.Card(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "还没有收纳盒", FontSize = 16, Foreground = Ui.TextPrimary },
                    Ui.Caption("点击“新建收纳盒”开始；也可以从桌面组件层的托盘菜单创建。"),
                },
            }));
            return page;
        }

        var list = new StackPanel { Spacing = 8 };
        foreach (var box in _snapshot.Boxes)
        {
            list.Children.Add(BoxCard(box));
        }

        page.Children.Add(Ui.Card(list, 12));
        page.Children.Add(new TextBlock
        {
            Text = "删除普通收纳盒前，应用会把其中的文件还原到原位置；映射/目标盒只移除引用，不删除源文件。",
            FontSize = 12,
            Foreground = Ui.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });
        return page;
    }

    private Border BoxCard(BoxSummary box)
    {
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 12,
        };

        var icon = new FontIcon
        {
            Glyph = box.Type switch
            {
                "Mapped" => "\uE71B",
                "Target" => "\uE8DE",
                _ => "\uE8B7",
            },
            FontSize = 22,
            Foreground = Ui.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(icon);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = box.Name,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Ui.TextPrimary,
        });
        var typeLabel = box.Type switch
        {
            "Mapped" => "映射收纳盒 · 只保存引用",
            "Target" => "目标收纳盒 · 绑定文件夹",
            _ => "普通收纳盒 · 文件真实移入",
        };
        text.Children.Add(new TextBlock
        {
            Text = $"{typeLabel} · {box.ItemCount} 项",
            FontSize = 12,
            Foreground = Ui.TextSecondary,
        });
        if (!string.IsNullOrEmpty(box.TargetPath))
        {
            text.Children.Add(new TextBlock
            {
                Text = box.TargetPath,
                FontSize = 11,
                Foreground = Ui.TextSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        Grid.SetColumn(text, 1);
        header.Children.Add(text);

        var visibility = new ToggleSwitch
        {
            IsOn = box.IsVisible,
            MinWidth = 0,
            OnContent = null,
            OffContent = null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        visibility.Toggled += async (_, _) =>
        {
            await RunCommandAsync(IpcCommands.SetBoxVisible, new VisibilityPayload
            {
                Id = box.Id,
                Visible = visibility.IsOn,
            });
        };
        Grid.SetColumn(visibility, 2);
        header.Children.Add(visibility);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(Ui.Button("重命名", async (_, _) => await RenameAsync(box), "\uE8AC"));
        buttons.Children.Add(Ui.Button("上移", async (_, _) => await MoveAsync(box, -1), "\uE74A"));
        buttons.Children.Add(Ui.Button("下移", async (_, _) => await MoveAsync(box, 1), "\uE74B"));
        buttons.Children.Add(Ui.Button("删除", async (_, _) => await DeleteAsync(box), "\uE74D"));

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(header);
        panel.Children.Add(buttons);
        return new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(6),
            Background = Ui.IsDark
                ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(8, 0, 0, 0)),
            Child = panel,
        };
    }

    private async Task CreateBoxAsync()
    {
        var name = Ui.TextInput("收纳盒", "例如：工作资料");
        var type = Ui.Combo(("Normal", "普通收纳盒 · 文件真实移入数据目录"),
            ("Mapped", "映射收纳盒 · 只保存路径引用"),
            ("Target", "目标收纳盒 · 绑定现有文件夹"));
        type.SelectedIndex = 0;

        var target = Ui.TextInput(string.Empty, "选择已有文件夹");
        target.IsReadOnly = true;
        var browse = Ui.Button("浏览…", async (_, _) =>
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle(_owner));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                target.Text = folder.Path;
            }
        });
        var targetRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 8,
        };
        targetRow.Children.Add(target);
        Grid.SetColumn(browse, 1);
        targetRow.Children.Add(browse);

        var targetPanel = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        targetPanel.Children.Add(new TextBlock { Text = "目标文件夹", FontSize = 12, Foreground = Ui.TextSecondary });
        targetPanel.Children.Add(targetRow);

        type.SelectionChanged += (_, _) =>
        {
            targetPanel.Visibility = Ui.ComboValue(type) == "Target" ? Visibility.Visible : Visibility.Collapsed;
        };

        var error = new TextBlock
        {
            Foreground = Ui.Danger,
            FontSize = 12,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "名称", FontSize = 12, Foreground = Ui.TextSecondary });
        content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = "类型", FontSize = 12, Foreground = Ui.TextSecondary });
        content.Children.Add(type);
        content.Children.Add(targetPanel);
        content.Children.Add(error);

        var dialog = _owner.CreateDialog("新建收纳盒", "创建", "取消", content);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (type.SelectedItem is ComboBoxItem item && (item.Tag as string) == "Target" &&
                !Directory.Exists(target.Text))
            {
                error.Text = "目标收纳盒需要选择一个已存在的文件夹。";
                error.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(IpcCommands.CreateBox, new CreateBoxPayload
            {
                Name = name.Text,
                Type = Ui.ComboValue(type) ?? "Normal",
                TargetPath = target.Text,
            });
        }, "收纳盒已创建");
    }

    private async Task RenameAsync(BoxSummary box)
    {
        var input = Ui.TextInput(box.Name);
        var dialog = _owner.CreateDialog("重命名收纳盒", "保存", "取消", input);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Text))
        {
            _owner.ShowError("名称不能为空。");
            return;
        }

        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(IpcCommands.RenameBox, new RenameBoxPayload
            {
                Id = box.Id,
                Name = input.Text.Trim(),
            });
        }, "名称已更新");
    }

    private async Task DeleteAsync(BoxSummary box)
    {
        var dialog = _owner.CreateDialog(
            "删除收纳盒",
            "删除",
            "取消",
            new TextBlock
            {
                Text = $"确定删除「{box.Name}」？\n\n" +
                       (box.Type == "Normal"
                           ? "其中的文件会先还原到原位置；若原位置不可用则回退到桌面。"
                           : "只会移除引用，不会改动源文件。"),
                TextWrapping = TextWrapping.Wrap,
            });
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(IpcCommands.DeleteBox, new IdPayload { Id = box.Id });
        }, "收纳盒已删除");
    }

    private async Task MoveAsync(BoxSummary box, int delta)
    {
        var ids = _snapshot.Boxes.Select(b => b.Id).ToList();
        var index = ids.IndexOf(box.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return;
        }

        (ids[index], ids[target]) = (ids[target], ids[index]);
        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(IpcCommands.ReorderBoxes, new ReorderPayload { OrderedIds = ids });
        });
    }

    private async Task RunCommandAsync(string command, object? payload, string? success = null)
    {
        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(command, payload);
        }, success);
    }
}
