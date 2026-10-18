using PocketDesk.Core;
using PocketDesk.Core.Tests.Infrastructure;

namespace PocketDesk.Core.Tests;

public sealed class RecoveryTests
{
    [Fact]
    public void RecoverPending_CompletesCopyWhenSourceAndDestinationIdentical()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateFile("移动中.txt", "payload");
        var destinationDirectory = Path.Combine(AppPaths.StorageRoot, "box-1");
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, "移动中.txt");
        File.Copy(source, destination);

        JournalRepository.Add(new JournalEntry
        {
            OpType = JournalOpType.MoveIn,
            BoxId = "box-1",
            FromPath = source,
            ToPath = destination,
        });

        var report = FileSystemService.RecoverPending();

        Assert.Equal(1, report.Recovered);
        Assert.Equal(0, report.Failed);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
        Assert.Empty(JournalRepository.GetPending());
    }

    [Fact]
    public void RecoverPending_RollsBackWhenDestinationMissing_AndKeepsSource()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateFile("未移动.txt", "payload");
        var destination = Path.Combine(AppPaths.StorageRoot, "missing", "未移动.txt");

        JournalRepository.Add(new JournalEntry
        {
            OpType = JournalOpType.MoveIn,
            FromPath = source,
            ToPath = destination,
        });

        var report = FileSystemService.RecoverPending();

        Assert.Equal(1, report.RolledBack);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Empty(JournalRepository.GetPending());
    }

    [Fact]
    public void MoveWithJournal_WritesDoneEntry()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateFile("日志.txt", "journal");
        var destination = Path.Combine(workspace.Root, "target", "日志.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        FileSystemService.MoveWithJournal(source, destination, JournalOpType.MoveIn);

        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(source));
        Assert.Empty(JournalRepository.GetPending());
    }
}
