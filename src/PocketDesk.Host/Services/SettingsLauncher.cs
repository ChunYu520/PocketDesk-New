using System.Diagnostics;
using PocketDesk.Core;

namespace PocketDesk.Host.Services;

/// <summary>启动独立的 WinUI 3 设置进程。</summary>
public static class SettingsLauncher
{
    public static void Launch(HostServices host)
    {
        try
        {
            var exe = HostServices.FindSettingsExecutable();
            if (exe is null)
            {
                host.ShowToast("未找到 PocketDesk.Settings.exe，请先构建设置界面。");
                LogService.Warn("未找到设置界面可执行文件。");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                host.ShowToast("设置界面启动失败：无法创建进程。");
                LogService.Warn($"设置界面进程创建失败：{exe}");
                return;
            }

            LogService.Info($"已启动设置界面：{exe} (PID {process.Id})");
        }
        catch (Exception ex)
        {
            LogService.Error("启动设置界面失败", ex);
            host.ShowToast($"启动设置界面失败：{ex.Message}");
        }
    }
}
