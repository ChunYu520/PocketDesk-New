using PocketDesk.Core;
using PocketDesk.Core.Tests.Infrastructure;

namespace PocketDesk.Core.Tests;

public sealed class ImportTests
{
    [Fact]
    public async Task NormalBox_MovesFileIntoStorage_AndJournalsCompletion()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("工作", BoxType.Normal, null);
        var source = workspace.CreateFile("报告.txt", "hello pocketdesk");

        var results = await ImportService.ImportPathsAsync(box, new[] { source }, SilentImportInteraction.Instance);

        Assert.True(results.Single().Success);
        Assert.False(File.Exists(source));
        var stored = Path.Combine(AppPaths.BoxStorage(box.Id), "报告.txt");
        Assert.True(File.Exists(stored));
        Assert.Equal("hello pocketdesk", File.ReadAllText(stored));

        var items = ItemRepository.GetByBox(box.Id);
        Assert.Single(items);
        Assert.Equal(ItemKind.File, items[0].Kind);
        Assert.Equal(PathUtil.Normalize(source), PathUtil.Normalize(items[0].SourcePath!));
        Assert.Empty(JournalRepository.GetPending());
    }

    [Fact]
    public async Task NormalBox_SameNameConflict_KeepBothCreatesUniqueName()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("工作", BoxType.Normal, null);
        var first = workspace.CreateFile("同名.txt", "one");
        await ImportService.ImportPathsAsync(box, new[] { first }, SilentImportInteraction.Instance);

        Directory.CreateDirectory(Path.Combine(workspace.Root, "second"));
        var second = Path.Combine(workspace.Root, "second", "同名.txt");
        await File.WriteAllTextAsync(second, "two");
        var results = await ImportService.ImportPathsAsync(box, new[] { second }, SilentImportInteraction.Instance);

        Assert.True(results.Single().Success);
        Assert.Equal(2, ItemRepository.GetByBox(box.Id).Count);
        var storage = AppPaths.BoxStorage(box.Id);
        Assert.True(File.Exists(Path.Combine(storage, "同名.txt")));
        Assert.True(File.Exists(Path.Combine(storage, "同名 (2).txt")));
    }

    [Fact]
    public async Task MappedBox_OnlyStoresReference_AndDeleteKeepsSource()
    {
        using var workspace = new TempWorkspace();
        var mapped = BoxService.CreateBox("映射", BoxType.Mapped, null);
        var source = workspace.CreateFile("参考.md", "# ref");

        var results = await ImportService.ImportPathsAsync(mapped, new[] { source }, SilentImportInteraction.Instance);
        Assert.True(results.Single().Success);
        Assert.True(File.Exists(source));

        var item = ItemRepository.GetByBox(mapped.Id).Single();
        Assert.True(item.IsReference);
        Assert.Equal(PathUtil.Normalize(source), PathUtil.Normalize(item.RealPath));

        BoxService.DeleteBox(mapped);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task DeleteItem_NormalBox_RestoresFileToOriginalLocation()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("工作", BoxType.Normal, null);
        var source = workspace.CreateFile("还原我.txt", "restore");
        await ImportService.ImportPathsAsync(box, new[] { source }, SilentImportInteraction.Instance);
        var item = ItemRepository.GetByBox(box.Id).Single();
        Assert.False(File.Exists(source));

        var deleted = BoxService.DeleteItems(box, new[] { item });

        Assert.Single(deleted);
        Assert.True(File.Exists(source));
        Assert.Equal("restore", File.ReadAllText(source));
        Assert.Empty(ItemRepository.GetByBox(box.Id));
    }

    [Fact]
    public async Task MoveItemsToBox_MovesFileAndUpdatesOwnership()
    {
        using var workspace = new TempWorkspace();
        var source = BoxService.CreateBox("源", BoxType.Normal, null);
        var target = BoxService.CreateBox("目标", BoxType.Normal, null);
        var file = workspace.CreateFile("移动.txt", "move-me");
        await ImportService.ImportPathsAsync(source, new[] { file }, SilentImportInteraction.Instance);
        var item = ItemRepository.GetByBox(source.Id).Single();

        var result = BoxService.MoveItemsToBox(source, target, new[] { item });

        Assert.Equal(1, result.Moved);
        Assert.Empty(ItemRepository.GetByBox(source.Id));
        var moved = ItemRepository.GetByBox(target.Id).Single();
        Assert.Equal(target.Id, moved.BoxId);
        Assert.True(File.Exists(moved.StoredPath));
        Assert.Equal("move-me", File.ReadAllText(moved.StoredPath!));
    }

    [Fact]
    public async Task ImportUrl_NormalBox_WritesUrlShortcut()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("网址", BoxType.Normal, null);

        var results = await ImportService.ImportUrlAsync(box, "https://example.com/docs", SilentImportInteraction.Instance);

        Assert.True(results.Single().Success);
        var item = ItemRepository.GetByBox(box.Id).Single();
        Assert.Equal(ItemKind.Url, item.Kind);
        Assert.True(File.Exists(item.StoredPath));
        Assert.Contains("URL=https://example.com/docs", File.ReadAllText(item.StoredPath!));
    }

    [Fact]
    public async Task RenameItem_NormalBox_RenamesStoredFile()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("工作", BoxType.Normal, null);
        var file = workspace.CreateFile("旧名.txt", "rename");
        await ImportService.ImportPathsAsync(box, new[] { file }, SilentImportInteraction.Instance);
        var item = ItemRepository.GetByBox(box.Id).Single();

        BoxService.RenameItem(box, item, "新名.txt");

        var updated = ItemRepository.GetByBox(box.Id).Single();
        Assert.Equal("新名.txt", updated.DisplayName);
        Assert.EndsWith("新名.txt", updated.StoredPath);
        Assert.True(File.Exists(updated.StoredPath));
    }
}
