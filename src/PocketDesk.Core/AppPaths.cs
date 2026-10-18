namespace PocketDesk.Core;

/// <summary>应用数据目录与文件路径。可被测试或便携模式覆盖。</summary>
public static class AppPaths
{
    private static readonly object Gate = new();
    private static string? _root;

    public static string Root
    {
        get
        {
            lock (Gate)
            {
                return _root ??= ResolveRoot();
            }
        }
    }

    public static string DatabasePath => Path.Combine(Root, "pocketdesk.db");
    public static string StorageRoot => Path.Combine(Root, "Storage");
    public static string TrashRoot => Path.Combine(Root, "Trash");
    public static string LogsDir => Path.Combine(Root, "Logs");
    public static string BackupDir => Path.Combine(Root, "backup");
    public static string IconCacheDir => Path.Combine(Root, "IconCache");
    public static string ExportDir => Path.Combine(Root, "Export");
    public static string PluginsDir => Path.Combine(Root, "Widgets");
    public static string ConfigDir => Path.Combine(Root, "Config");

    public static string BoxStorage(string boxId) => Path.Combine(StorageRoot, boxId);

    /// <summary>测试/便携模式：在访问任何路径前调用。</summary>
    public static void OverrideRoot(string root)
    {
        lock (Gate)
        {
            _root = Path.GetFullPath(root);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _root = null;
        }
    }

    private static string ResolveRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("POCKETDESK_HOME");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%");
        }

        // 新宿主使用独立目录，避免与旧版 PocketDesk 的 schema 和 journal 互相干扰。
        return Path.Combine(localAppData, "PocketDesk-New");
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(StorageRoot);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(BackupDir);
        Directory.CreateDirectory(IconCacheDir);
        Directory.CreateDirectory(PluginsDir);
        Directory.CreateDirectory(ConfigDir);
    }

    public static string DesktopPath
    {
        get
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                desktop = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Desktop");
            }

            return desktop;
        }
    }

    public static string DownloadsPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile
            ? Path.Combine(profile, "Downloads")
            : DesktopPath;

    public static string NewDiagnosticsArchivePath() =>
        Path.Combine(ExportDir, $"PocketDesk-New-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    /// <summary>把日志文本中的用户路径替换为占位符，避免导出真实文件路径。</summary>
    public static string SanitizeForLog(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        text = text.Replace(Root, "%LOCALAPPDATA%\\PocketDesk-New", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
