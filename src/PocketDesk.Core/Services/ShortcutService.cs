using System.Text;

namespace PocketDesk.Core;

/// <summary>快捷方式（.lnk / .url）解析与创建。</summary>
public static class ShortcutService
{
    public sealed record ShortcutInfo(string? TargetPath, string? Arguments, string? WorkingDirectory, string? Url);

    public static ShortcutInfo Resolve(string path)
    {
        var extension = Path.GetExtension(path);
        try
        {
            if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                return new ShortcutInfo(null, null, null, ReadUrl(path));
            }

            if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return new ShortcutInfo(null, null, null, null);
            }

            var clsid = ShellNative.CLSID_ShellLink;
            var iid = ShellNative.IID_IPersistFile;
            var hr = ShellNative.CoCreateInstance(
                ref clsid,
                IntPtr.Zero,
                ShellNative.CLSCTX_INPROC_SERVER,
                ref iid,
                out var comObject);
            if (hr != 0 || comObject is null)
            {
                return new ShortcutInfo(null, null, null, null);
            }

            var persist = (ShellNative.IPersistFile)comObject;
            persist.Load(path, ShellNative.STGM_READ);

            var link = (ShellNative.IShellLinkW)comObject;
            link.Resolve(IntPtr.Zero, ShellNative.SLR_NO_UI);

            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var arguments = new StringBuilder(1024);
            link.GetArguments(arguments, arguments.Capacity);
            var workingDirectory = new StringBuilder(1024);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            return new ShortcutInfo(
                target.Length > 0 ? target.ToString() : null,
                arguments.Length > 0 ? arguments.ToString() : null,
                workingDirectory.Length > 0 ? workingDirectory.ToString() : null,
                null);
        }
        catch (Exception ex)
        {
            LogService.Warn($"解析快捷方式失败：{Path.GetFileName(path)} :: {ex.GetType().Name}");
            return new ShortcutInfo(null, null, null, null);
        }
    }

    public static string? ReadUrl(string urlFilePath)
    {
        try
        {
            foreach (var line in File.ReadLines(urlFilePath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line[4..].Trim();
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public static bool CreateShortcut(
        string linkPath,
        string targetPath,
        string? workingDirectory = null,
        string? arguments = null,
        string? description = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath) ?? AppPaths.DesktopPath);
            var clsid = ShellNative.CLSID_ShellLink;
            var iid = ShellNative.IID_IShellLinkW;
            var hr = ShellNative.CoCreateInstance(
                ref clsid,
                IntPtr.Zero,
                ShellNative.CLSCTX_INPROC_SERVER,
                ref iid,
                out var comObject);
            if (hr != 0 || comObject is null)
            {
                return false;
            }

            var link = (ShellNative.IShellLinkW)comObject;
            link.SetPath(targetPath);
            link.SetWorkingDirectory(
                string.IsNullOrEmpty(workingDirectory) ? Path.GetDirectoryName(targetPath) ?? string.Empty : workingDirectory);
            if (!string.IsNullOrEmpty(arguments))
            {
                link.SetArguments(arguments);
            }

            if (!string.IsNullOrEmpty(description))
            {
                link.SetDescription(description);
            }

            var persist = (ShellNative.IPersistFile)comObject;
            persist.Save(linkPath, true);
            LogService.Info($"已创建快捷方式：{Path.GetFileName(linkPath)}");
            return File.Exists(linkPath);
        }
        catch (Exception ex)
        {
            LogService.Error("创建快捷方式失败", ex);
            return false;
        }
    }
}
