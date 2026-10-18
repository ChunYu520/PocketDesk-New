using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PocketDesk.Core;

namespace PocketDesk.Host.Services;

/// <summary>托盘入口：设置、显示/隐藏组件、新建收纳盒、退出。</summary>
public sealed class TrayService : IDisposable
{
    private readonly HostServices _host;
    private TrayIcon? _tray;

    public TrayService(HostServices host)
    {
        _host = host;
    }

    public void Install()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            var icon = File.Exists(iconPath)
                ? new WindowIcon(iconPath)
                : null;

            var menu = new NativeMenu();
            menu.Add(MenuItem("打开设置", () => _host.OpenSettings()));
            menu.Add(MenuItem("新建收纳盒", () => CreateBox()));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(MenuItem("显示全部组件", () => ShowAll(true)));
            menu.Add(MenuItem("隐藏全部组件", () => ShowAll(false)));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(MenuItem("打开数据目录", () => ShellService.Open(AppPaths.Root)));
            menu.Add(MenuItem("退出 PocketDesk", () => _host.Shutdown()));

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "PocketDesk-New · 桌面组件层",
                Menu = menu,
                IsVisible = true,
            };
            _tray.Clicked += (_, _) => _host.OpenSettings();

            if (Application.Current is { } app)
            {
                TrayIcon.SetIcons(app, new TrayIcons { _tray });
            }

            LogService.Info("托盘图标已创建");
        }
        catch (Exception ex)
        {
            LogService.Warn($"创建托盘图标失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static NativeMenuItem MenuItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private void CreateBox()
    {
        try
        {
            _host.ViewModel.AddBox(null, BoxType.Normal, null);
            _host.ShowToast("已新建收纳盒");
        }
        catch (Exception ex)
        {
            LogService.Error("新建收纳盒失败", ex);
        }
    }

    private void ShowAll(bool visible)
    {
        try
        {
            foreach (var box in BoxRepository.GetAll())
            {
                BoxRepository.SetVisibility(box.Id, visible);
            }

            _host.ViewModel.ReloadAll();
            _host.Window?.SyncCards();
        }
        catch (Exception ex)
        {
            LogService.Error("切换组件显示状态失败", ex);
        }
    }

    public void Dispose()
    {
        if (_tray is null)
        {
            return;
        }

        try
        {
            _tray.IsVisible = false;
            if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons)
            {
                icons.Remove(_tray);
            }

            _tray.Dispose();
        }
        catch
        {
            // ignore
        }

        _tray = null;
    }
}
