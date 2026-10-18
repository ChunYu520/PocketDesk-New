using Avalonia.Controls;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets;

namespace PocketDesk.Host.ViewModels;

/// <summary>注入给组件的宿主上下文。组件只通过该上下文读写设置与请求宿主能力。</summary>
public sealed class WidgetContext : IWidgetContext
{
    private readonly CardViewModel _card;
    private readonly HostServices _host;

    public WidgetContext(CardViewModel card, HostServices host)
    {
        _card = card;
        _host = host;
    }

    public WidgetInstance Instance => _card.Instance;

    public AppSettings GlobalSettings => AppSettingsService.Current;

    public IServiceProvider Services => _host.ServiceProvider;

    public event EventHandler? SettingsChanged;

    public string? GetSetting(string key, string? fallback = null) =>
        Instance.GetSetting(key) ?? fallback;

    public void SetSetting(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Instance.Settings.Remove(key);
        }
        else
        {
            Instance.Settings[key] = value;
        }

        _host.SaveWidgetSettings(_card);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyMetadataChanged() => _card.RefreshFromModel();

    public void RequestKeyboardFocus() => _host.RequestInputFocus(_card, null);

    internal void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);
}
