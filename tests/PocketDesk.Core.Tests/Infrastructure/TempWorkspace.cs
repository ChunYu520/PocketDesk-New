using Microsoft.Data.Sqlite;
using PocketDesk.Core;

namespace PocketDesk.Core.Tests.Infrastructure;

/// <summary>每个测试使用独立数据目录，避免污染真实 %LOCALAPPDATA% 与旧版 PocketDesk。</summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "PocketDesk-New.Tests", Guid.NewGuid().ToString("N"));
        SourceDir = Path.Combine(Root, "sources");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SourceDir);

        AppPaths.OverrideRoot(Root);
        Database.Reset();
        LogService.Reset();
        Database.Initialize();
        AppSettingsService.Load();
    }

    public string Root { get; }

    public string SourceDir { get; }

    public string CreateFile(string name, string content = "test-content")
    {
        var path = Path.Combine(SourceDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateFolder(string name)
    {
        var path = Path.Combine(SourceDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Database.Reset();
            SqliteConnection.ClearAllPools();
            AppPaths.Reset();
            LogService.Reset();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (Directory.Exists(Root))
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Windows 上偶尔仍有文件句柄未释放，留给系统清理临时目录。
        }
    }
}
