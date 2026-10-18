using System.Diagnostics;

namespace PocketDesk.Core;

/// <summary>系统 Shell 交互（打开、定位、属性）。剪贴板属于 UI 层，由宿主实现。</summary>
public static class ShellService
{
    public static void Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            LogService.Error("打开失败", ex);
        }
    }

    public static void OpenUrl(string url) => Open(url);

    public static void RevealInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                });
                return;
            }

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
                return;
            }

            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                RevealInExplorer(parent);
            }
            else
            {
                LogService.Warn("无法定位：路径不存在");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("打开所在位置失败", ex);
        }
    }

    public static void ShowProperties(nint hwnd, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !PathUtil.Exists(path))
        {
            return;
        }

        try
        {
            ShellNative.SHObjectProperties(hwnd, ShellNative.SHOP_FILEPATH, path, null);
        }
        catch (Exception ex)
        {
            LogService.Error("打开属性失败", ex);
        }
    }

    /// <summary>在桌面创建指向目标文件/文件夹的快捷方式，或网址的 .url 文件。</summary>
    public static string? CreateShortcutOnDesktop(string displayName, string? targetPath, string? url)
    {
        try
        {
            var baseName = PathUtil.SanitizeFileName($"{displayName} - 快捷方式", "快捷方式");
            if (!string.IsNullOrEmpty(url))
            {
                FileSystemService.WriteUrlShortcut(AppPaths.DesktopPath, url, baseName, out var createdUrl);
                return createdUrl;
            }

            if (string.IsNullOrEmpty(targetPath) || !PathUtil.Exists(targetPath))
            {
                return null;
            }

            var linkPath = PathUtil.UniquePathIn(AppPaths.DesktopPath, baseName + ".lnk");
            return ShortcutService.CreateShortcut(linkPath, targetPath, Path.GetDirectoryName(targetPath), null, displayName)
                ? linkPath
                : null;
        }
        catch (Exception ex)
        {
            LogService.Error("创建桌面快捷方式失败", ex);
            return null;
        }
    }
}
