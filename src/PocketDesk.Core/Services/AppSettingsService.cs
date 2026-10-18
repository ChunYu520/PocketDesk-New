using PocketDesk.Shared;

namespace PocketDesk.Core;

/// <summary>应用设置的单例访问与变更通知。</summary>
public static class AppSettingsService
{
    private const string StorageKey = "app";

    private static AppSettings _current = new();

    public static AppSettings Current => _current;

    public static event Action? Changed;

    public static void Load()
    {
        var json = SettingsRepository.Get(StorageKey);
        _current = Json.Deserialize<AppSettings>(json) ?? new AppSettings();
        _current.Normalize();
    }

    public static void Save()
    {
        _current.Normalize();
        SettingsRepository.Set(StorageKey, Json.Serialize(_current));
        Changed?.Invoke();
    }

    public static void Update(Action<AppSettings> mutate)
    {
        mutate(_current);
        Save();
    }

    /// <summary>用外部（设置界面）传来的完整设置替换并保存。</summary>
    public static void Replace(AppSettings settings)
    {
        _current = settings.Clone();
        Save();
    }
}
