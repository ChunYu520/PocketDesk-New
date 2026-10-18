using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using PocketDesk.Settings.Services;
using PocketDesk.Settings.Views;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WinRT.Interop;

namespace PocketDesk.Settings;

public sealed partial class MainWindow : Window
{
    private readonly SettingsClient _client = new();
    private SettingsSnapshot? _snapshot;
    private string _selectedTag = "general";
    private DispatcherQueueTimer? _saveTimer;
    private AppSettings? _pendingSettings;
    private readonly string? _captureDirectory;
    private bool _capturing;

    public MainWindow()
    {
        InitializeComponent();

        _captureDirectory = GetOption(Environment.GetCommandLineArgs(), "--capture");

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var width = Math.Min((int)(work.Width * 0.8), 2080);
        var height = Math.Min((int)(work.Height * 0.86), 1440);
        appWindow.MoveAndResize(new RectInt32(
            work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2,
            width,
            height));
        appWindow.Title = "PocketDesk 设置";

        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += OnSaveTimerTick;

        Nav.Loaded += async (_, _) =>
        {
            if (Nav.MenuItems.FirstOrDefault() is NavigationViewItem first)
            {
                Nav.SelectedItem = first;
            }

            await RefreshAsync();
            if (_captureDirectory is not null && !_capturing)
            {
                await CapturePagesAsync(_captureDirectory);
            }
        };
        Closed += (_, _) => _client.Dispose();
    }

    public SettingsClient Client => _client;

    public SettingsSnapshot? Snapshot => _snapshot;

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _selectedTag = tag;
            ShowPage(tag);
        }
    }

    public async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            _snapshot = await _client.GetSnapshotAsync();
            StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.SeaGreen);
            StatusText.Text = $"已连接 · 宿主 v{_snapshot.HostVersion}";
            RefreshButton.Content = "刷新";
            ShowPage(_selectedTag);
        }
        catch (Exception ex)
        {
            _snapshot = null;
            StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            StatusText.Text = "未连接宿主";
            RefreshButton.Content = "重试";
            ShowDisconnected(ex.Message);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ShowDisconnected(string message)
    {
        ContentHost.Children.Clear();
        var panel = new StackPanel { Spacing = 16, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Ui.PageTitle("无法连接桌面组件层"));
        panel.Children.Add(Ui.PageSubtitle(
            "设置界面通过命名管道与 PocketDesk 宿主通信。请先启动 PocketDesk.Host，再点击左侧的“重新连接”。"));
        panel.Children.Add(Ui.Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Ui.TextSecondary },
                Ui.Caption("如果宿主刚刚启动，通常几秒内即可连接。"),
            },
        }));
        var retry = Ui.PrimaryButton("重新连接", async (_, _) => await RefreshAsync());
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(retry);
        ContentHost.Children.Add(panel);
        ShowMessage(InfoBarSeverity.Warning, "设置界面当前为离线状态", message);
    }

    private void ShowPage(string tag)
    {
        if (_snapshot is null)
        {
            return;
        }

        ContentHost.Children.Clear();
        FrameworkElement page = tag switch
        {
            "widgets" => new WidgetsPage(this, _snapshot),
            "boxes" => new BoxesPage(this, _snapshot),
            "storage" => new StoragePage(this, _snapshot),
            _ => new GeneralPage(this, _snapshot),
        };
        ContentHost.Children.Add(page);
    }

    public void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        MessageBar.Severity = severity;
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }

    public void ShowError(string message) => ShowMessage(InfoBarSeverity.Error, "操作失败", message);

    public void ShowSuccess(string message) => ShowMessage(InfoBarSeverity.Success, "已完成", message);

    /// <summary>滑块等连续输入使用防抖，避免每次像素变化都写数据库。</summary>
    public void QueueSettingsSave(AppSettings settings)
    {
        _pendingSettings = settings;
        if (_saveTimer is null)
        {
            return;
        }

        if (_saveTimer.IsRunning)
        {
            _saveTimer.Stop();
        }

        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_pendingSettings is null)
        {
            return;
        }

        try
        {
            await _client.SendCommandAsync(IpcCommands.UpdateSettings, new UpdateSettingsPayload
            {
                Settings = _pendingSettings,
            });

            if (_snapshot is not null)
            {
                _snapshot.Settings = _pendingSettings.Clone();
            }

            StatusText.Text = $"已连接 · 设置已保存 {DateTime.Now:HH:mm:ss}";
            RootGrid.RequestedTheme = _pendingSettings.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _pendingSettings = null;
        }
    }

    public async Task SaveWidgetSettingsAsync(string instanceId, Dictionary<string, string> settings)
    {
        await _client.SendCommandAsync(IpcCommands.UpdateWidgetSettings, new UpdateWidgetSettingsPayload
        {
            InstanceId = instanceId,
            Settings = settings,
        });

        if (_snapshot is not null)
        {
            var instance = _snapshot.Widgets.FirstOrDefault(w => w.Id == instanceId);
            if (instance is not null)
            {
                instance.Settings = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public async Task RunAsync(Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
            if (successMessage is not null)
            {
                ShowSuccess(successMessage);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    public async Task RunWithoutRefreshAsync(Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
            if (successMessage is not null)
            {
                ShowSuccess(successMessage);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    public static nint WindowHandle(Window window) => WindowNative.GetWindowHandle(window);

    private async Task CapturePagesAsync(string directory)
    {
        _capturing = true;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var tag in new[] { "general", "widgets", "boxes", "storage" })
            {
                var item = Nav.MenuItems.OfType<NavigationViewItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                {
                    Nav.SelectedItem = item;
                }

                _selectedTag = tag;
                ShowPage(tag);
                await Task.Delay(650);
                await CaptureAsync(Path.Combine(directory, $"settings-{tag}.png"), RootGrid);
            }
        }
        catch (Exception ex)
        {
            ShowError($"截图失败：{ex.Message}");
        }
        finally
        {
            _capturing = false;
            Application.Current.Exit();
        }
    }

    private static async Task CaptureAsync(string path, FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        await File.WriteAllBytesAsync(path, Array.Empty<byte>());

        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth,
            (uint)bitmap.PixelHeight,
            96,
            96,
            pixels.ToArray());
        await encoder.FlushAsync();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }

    public ContentDialog CreateDialog(string title, string primaryText, string closeText, UIElement content)
    {
        return new ContentDialog
        {
            Title = title,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary,
            Content = content,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Default,
        };
    }
}
