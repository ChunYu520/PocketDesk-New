using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;

namespace PocketDesk.Settings.Views;

/// <summary>存储路径、诊断与宿主生命周期。</summary>
public sealed class StoragePage : UserControl
{
    private readonly MainWindow _owner;
    private readonly SettingsSnapshot _snapshot;

    public StoragePage(MainWindow owner, SettingsSnapshot snapshot)
    {
        _owner = owner;
        _snapshot = snapshot;
        Content = Build();
    }

    private FrameworkElement Build()
    {
        var page = new StackPanel { Spacing = 0 };
        page.Children.Add(Ui.PageTitle("存储与诊断"));
        page.Children.Add(Ui.PageSubtitle(
            "宿主使用 SQLite + WAL 保存收纳盒、收纳项、journal 与组件配置。设置界面不直接操作数据库，所有操作经 IPC 完成。"));

        page.Children.Add(Ui.SectionTitle("数据位置"));
        page.Children.Add(Ui.Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                InfoRow("数据根目录", _snapshot.DataRoot),
                InfoRow("数据库", _snapshot.DatabasePath),
                InfoRow("宿主版本", _snapshot.HostVersion),
                InfoRow("宿主启动时间", _snapshot.HostStartedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                InfoRow("恢复状态", _snapshot.LastRecoveryMessage ?? "没有待恢复的文件移动"),
            },
        }));

        page.Children.Add(Ui.SectionTitle("操作"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(ButtonWithMargin(Ui.Button("打开数据目录", async (_, _) =>
            await _owner.RunWithoutRefreshAsync(async () =>
                await _owner.Client.SendCommandAsync(IpcCommands.OpenDataDirectory))), "\uE8B7"));
        buttons.Children.Add(ButtonWithMargin(Ui.Button("打开日志目录", async (_, _) =>
            await _owner.RunWithoutRefreshAsync(async () =>
                await _owner.Client.SendCommandAsync(IpcCommands.OpenLogDirectory))), "\uE8A5"));
        buttons.Children.Add(ButtonWithMargin(Ui.Button("导出诊断日志", async (_, _) =>
        {
            try
            {
                var result = await _owner.Client.SendAsync<DiagnosticsResult>(IpcCommands.ExportDiagnostics);
                if (result is not null)
                {
                    _owner.ShowSuccess($"诊断日志已导出：{result.Path}");
                }
            }
            catch (Exception ex)
            {
                _owner.ShowError(ex.Message);
            }
        }), "\uE896"));
        page.Children.Add(Ui.Card(buttons));

        page.Children.Add(Ui.SectionTitle("退出宿主"));
        var shutdownPanel = new StackPanel { Spacing = 10 };
        shutdownPanel.Children.Add(new TextBlock
        {
            Text = "退出后桌面组件层会关闭，设置界面将无法连接。下次可从开始菜单或 PocketDesk.Host.exe 重新启动。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ui.TextSecondary,
            FontSize = 12,
        });
        shutdownPanel.Children.Add(Ui.Button("退出 PocketDesk 宿主", async (_, _) =>
        {
            var dialog = _owner.CreateDialog("退出宿主", "退出", "取消", new TextBlock
            {
                Text = "确定退出 PocketDesk 组件层？",
                TextWrapping = TextWrapping.Wrap,
            });
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await _owner.Client.SendCommandAsync(IpcCommands.Shutdown);
                    _owner.ShowMessage(InfoBarSeverity.Informational, "宿主已退出", "可以关闭此设置窗口。");
                }
                catch (Exception ex)
                {
                    _owner.ShowError(ex.Message);
                }
            }
        }, "\uE7E8"));
        page.Children.Add(Ui.Card(shutdownPanel));

        page.Children.Add(Ui.SectionTitle("关于数据安全"));
        page.Children.Add(Ui.Card(new TextBlock
        {
            Text = "普通收纳盒会把文件真实移动到数据目录的 Storage/<收纳盒 Id>，移动前写入 journal，启动时自动恢复未完成操作。" +
                   "映射收纳盒只保存绝对路径，绝不移动、复制或修改源文件。目标收纳盒绑定现有文件夹，内容双向显示。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ui.TextSecondary,
            FontSize = 12,
        }));

        return page;
    }

    private static FrameworkElement InfoRow(string label, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = Ui.TextSecondary,
            VerticalAlignment = VerticalAlignment.Top,
        });
        var block = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = Ui.TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(block, 1);
        grid.Children.Add(block);
        return grid;
    }

    private static FrameworkElement ButtonWithMargin(Button button, string icon)
    {
        button.Margin = new Thickness(0, 0, 8, 8);
        return button;
    }
}
