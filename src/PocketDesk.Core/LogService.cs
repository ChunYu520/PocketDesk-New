using System.IO.Compression;
using System.Text;
using PocketDesk.Shared;

namespace PocketDesk.Core;

/// <summary>轻量文件日志。仅记录应用状态与异常，不记录文件内容。</summary>
public static class LogService
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static string _logFile = string.Empty;

    public const long MaxFileBytes = 2 * 1024 * 1024;

    public static void Initialize()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                _logFile = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
                _initialized = true;
            }
            catch
            {
                _initialized = false;
            }
        }

        Info($"PocketDesk-New 启动，版本 {AppInfo.Version}，OS {Environment.OSVersion.VersionString}，{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _initialized = false;
            _logFile = string.Empty;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        var text = ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                if (!_initialized)
                {
                    return;
                }

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {AppPaths.SanitizeForLog(message)}{Environment.NewLine}";
                File.AppendAllText(_logFile, line, Encoding.UTF8);
                RotateIfNeeded();
            }
        }
        catch
        {
            // 日志失败不能影响主流程。
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logFile);
            if (!info.Exists || info.Length < MaxFileBytes)
            {
                return;
            }

            var archive = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.Move(_logFile, archive, overwrite: true);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>导出最近日志与摘要到 zip，返回生成的文件路径。</summary>
    public static string ExportDiagnostics()
    {
        Directory.CreateDirectory(AppPaths.ExportDir);
        var target = AppPaths.NewDiagnosticsArchivePath();
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDir, "*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(5))
            {
                archive.CreateEntryFromFile(file, $"logs/{Path.GetFileName(file)}");
            }

            var summary = new StringBuilder()
                .AppendLine("PocketDesk-New 诊断摘要")
                .AppendLine($"版本: {AppInfo.Version}")
                .AppendLine($"OS: {Environment.OSVersion.VersionString}")
                .AppendLine($"架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}")
                .AppendLine($"数据目录: {AppPaths.Root}")
                .AppendLine($"生成时间: {DateTime.Now:O}")
                .AppendLine("说明: 日志仅包含应用状态与异常，不包含文件内容。")
                .ToString();
            var entry = archive.CreateEntry("summary.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(summary);
        }

        return target;
    }
}
