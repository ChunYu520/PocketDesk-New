using PocketDesk.Core;
using PocketDesk.Core.Tests.Infrastructure;

namespace PocketDesk.Core.Tests;

public sealed class BoxTests
{
    [Fact]
    public void CreateBox_PersistsAndAppliesDefaults()
    {
        using var workspace = new TempWorkspace();

        var box = BoxService.CreateBox("工作资料", BoxType.Normal, null);

        Assert.Equal("工作资料", box.Name);
        Assert.Equal(BoxType.Normal, box.Type);
        Assert.NotNull(box.Placement);
        Assert.True(box.IsVisible);
        Assert.Equal(box.Id, BoxRepository.Get(box.Id)!.Id);
    }

    [Fact]
    public void RenameAndVisibility_ArePersisted()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("旧名称", BoxType.Normal, null);

        BoxService.RenameBox(box, "新名称");
        BoxService.SetVisible(box, false);

        var fresh = BoxRepository.Get(box.Id)!;
        Assert.Equal("新名称", fresh.Name);
        Assert.False(fresh.IsVisible);
    }

    [Fact]
    public void DeleteBox_RemovesRowAndStorage()
    {
        using var workspace = new TempWorkspace();
        var box = BoxService.CreateBox("待删除", BoxType.Normal, null);
        var storage = AppPaths.BoxStorage(box.Id);
        Directory.CreateDirectory(storage);

        BoxService.DeleteBox(box);

        Assert.Null(BoxRepository.Get(box.Id));
        Assert.False(Directory.Exists(storage));
    }

    [Fact]
    public void CreateTargetBox_RequiresExistingDirectory()
    {
        using var workspace = new TempWorkspace();

        Assert.Throws<InvalidOperationException>(() =>
            BoxService.CreateBox("目标", BoxType.Target, Path.Combine(workspace.Root, "不存在")));

        var target = workspace.CreateFolder("goal");
        var box = BoxService.CreateBox("目标", BoxType.Target, target);
        Assert.Equal(PathUtil.Normalize(target), box.TargetPath);
    }

    [Fact]
    public void TargetBox_SynchronizesExistingTopLevelItems()
    {
        using var workspace = new TempWorkspace();
        var target = workspace.CreateFolder("目标文件夹");
        File.WriteAllText(Path.Combine(target, "目标.md"), "# goal");
        Directory.CreateDirectory(Path.Combine(target, "子目录"));

        var box = BoxService.CreateBox("项目目标", BoxType.Target, target);
        var items = BoxService.GetItems(box);

        Assert.Contains(items, i => i.DisplayName == "目标.md");
        Assert.Contains(items, i => i.DisplayName == "子目录" && i.Kind == ItemKind.Folder);
        Assert.All(items, i => Assert.True(i.IsReference));
    }

    [Fact]
    public async Task DuplicateBox_CopiesConfigurationButNotItems()
    {
        using var workspace = new TempWorkspace();
        var source = BoxService.CreateBox("源", BoxType.Normal, null);
        var file = workspace.CreateFile("a.txt");
        await ImportService.ImportPathsAsync(source, new[] { file }, SilentImportInteraction.Instance);

        var copy = BoxService.DuplicateBox(source);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Contains("副本", copy.Name);
        Assert.Empty(ItemRepository.GetByBox(copy.Id));
        Assert.Single(ItemRepository.GetByBox(source.Id));
    }
}
