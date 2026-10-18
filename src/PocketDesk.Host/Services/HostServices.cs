using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using PocketDesk.Core;
using PocketDesk.Host.ViewModels;
using PocketDesk.Host.Widgets;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;

namespace PocketDesk.Host.Services;

/// <summary>宿主组合根：组件注册、主题、窗口、IPC 与托盘的生命周期。</summary>
public sealed class HostServices
{
    public static HostServices? Current { get; private set; }

    public WidgetRegistry Widgets { get; } = new();

    public IconLoader Icons { get; } = new();

    public ThemeService Theme { get; } = new();

    public MainViewModel ViewModel { get; }

    public IServiceProvider ServiceProvider { get; private set; } = null!;

    public MainWindow? Window { get; private set; }

    public IpcServer? Ipc { get; private set; }

    public TrayService? Tray { get; private set; }

    public DateTime StartedAt { get; } = DateTime.Now;

    public bool IsShuttingDown { get; private set; }

    public nint WindowHandle => Window?.WindowHandle ?? IntPtr.Zero;

    public HostServices()
    {
        Current = this;
        ViewModel = new MainViewModel(this);
    }

    public void Initialize()
    {
        Widgets.Register(new BoxWidget());

        var builtInPlugins = Path.Combine(AppContext.BaseDirectory, "Widgets");
        Widgets.LoadPlugins(builtInPlugins);
        if (!string.Equals(Path.GetFullPath(AppPaths.PluginsDir), Path.GetFullPath(builtInPlugins), StringComparison.OrdinalIgnoreCase))
        {
            Widgets.LoadPlugins(AppPaths.PluginsDir);
        }

        Theme.Apply(AppSettingsService.Current);

        var services = new ServiceCollection();
        services.AddSingleton(this);
        services.AddSingleton(Widgets);
        services.AddSingleton(Icons);
        services.AddSingleton(Theme);
        services.AddSingleton(ViewModel);
        ServiceProvider = services.BuildServiceProvider();

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) => Theme.Apply(AppSettingsService.Current);
        }
    }

    public void AttachWindow(MainWindow window) => Window = window;

    public void Start()
    {
        Ipc = new IpcServer(this);
        Ipc.Start();

        Tray = new TrayService(this);
        Tray.Install();
    }

    public void ShowToast(string message) => Window?.ShowToast(message);

    public void OpenSettings() => SettingsLauncher.Launch(this);

    public void RequestInputFocus(CardViewModel? card = null, Control? target = null) =>
        Window?.RequestInputFocus(card, target);

    public void BeginItemDrag(CardViewModel source, IReadOnlyList<ItemViewModel> items, Avalonia.Input.PointerEventArgs e) =>
        Window?.BeginItemDrag(source, items, e);

    public void SaveWidgetSettings(CardViewModel card)
    {
        if (card.IsBox)
        {
            return;
        }

        WidgetRepository.UpdateSettings(card.Instance.Id, card.Instance.Settings);
        WidgetRepository.Update(card.Instance);
    }

    public void UpdateSettings(AppSettings settings)
    {
        ViewModel.ApplySettings(settings);

        // 非收纳盒组件通常直接使用全局强调色/主题，设置变更后重建其内容视图。
        foreach (var card in ViewModel.Cards.Where(c => !c.IsBox))
        {
            card.Content = card.Widget.CreateView(card.Context!);
            Window?.RefreshCardContent(card);
        }

        Window?.ApplyTheme();
    }

    public void ApplyAppearanceToAll()
    {
        Theme.Apply(AppSettingsService.Current);
        foreach (var card in ViewModel.Cards)
        {
            card.ApplyAppearance();
        }

        Window?.ApplyTheme();
    }

    public SettingsSnapshot CreateSnapshot() => ViewModel.CreateSnapshot();

    public void ExportDiagnostics()
    {
        try
        {
            var path = LogService.ExportDiagnostics();
            ShowToast($"诊断日志已导出：{path}");
            ShellService.RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            LogService.Error("导出诊断日志失败", ex);
            ShowToast($"导出失败：{ex.Message}");
        }
    }

    public void Shutdown()
    {
        if (IsShuttingDown)
        {
            return;
        }

        IsShuttingDown = true;
        try
        {
            ViewModel.SaveAllPlacements();
        }
        catch (Exception ex)
        {
            LogService.Warn($"退出时保存布局失败：{ex.Message}");
        }

        Ipc?.Dispose();
        Tray?.Dispose();
        Window?.Close();

        LogService.Info("PocketDesk-New 宿主退出");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public static string? FindSettingsExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("POCKETDESK_SETTINGS_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Settings", "PocketDesk.Settings.exe"),
            Path.Combine(baseDir, "PocketDesk.Settings.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "PocketDesk.Settings", "bin", "Debug",
                "net10.0-windows10.0.26100.0", "win-arm64", "PocketDesk.Settings.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "PocketDesk.Settings", "bin", "Debug",
                "net10.0-windows10.0.26100.0", "win-x64", "PocketDesk.Settings.exe")),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var devRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        var settingsBin = Path.Combine(devRoot, "PocketDesk.Settings", "bin");
        if (Directory.Exists(settingsBin))
        {
            return Directory.EnumerateFiles(settingsBin, "PocketDesk.Settings.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        return null;
    }
}
