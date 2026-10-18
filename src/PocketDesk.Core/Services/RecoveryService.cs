namespace PocketDesk.Core;

/// <summary>启动恢复结果。</summary>
public sealed class RecoveryReport
{
    public int Recovered { get; init; }
    public int RolledBack { get; init; }
    public int Failed { get; init; }

    public bool HasIssues => RolledBack > 0 || Failed > 0;

    public string ToDisplayString() =>
        Failed > 0
            ? $"启动时恢复未完成文件移动：{Recovered} 项完成，{RolledBack} 项回滚，{Failed} 项失败。请检查收纳盒内容。"
            : $"已恢复未完成的文件移动：{Recovered} 项完成，{RolledBack} 项回滚。";
}

/// <summary>启动时的数据自检。</summary>
public static class RecoveryService
{
    public static RecoveryReport RecoverOnStartup()
    {
        var (recovered, rolledBack, failed) = FileSystemService.RecoverPending();
        var report = new RecoveryReport
        {
            Recovered = recovered,
            RolledBack = rolledBack,
            Failed = failed,
        };

        if (report.HasIssues)
        {
            LogService.Warn(report.ToDisplayString());
        }
        else if (recovered > 0 || rolledBack > 0)
        {
            LogService.Info(report.ToDisplayString());
        }

        return report;
    }
}
