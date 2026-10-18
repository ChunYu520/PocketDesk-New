using Avalonia.Controls;
using PocketDesk.Core;
using PocketDesk.Host.Services;

namespace PocketDesk.Host.Views;

/// <summary>把核心层的冲突/大文件决策接到 Avalonia 对话框。</summary>
public sealed class UiImportInteraction : IImportInteraction
{
    private readonly Window _owner;
    private readonly HostServices _host;

    public UiImportInteraction(Window owner, HostServices host)
    {
        _owner = owner;
        _host = host;
    }

    public Task<ConflictChoice> ResolveConflictAsync(string fileName, string destinationPath) =>
        DialogService.ConflictAsync(_owner, fileName, destinationPath);

    public Task<bool> ConfirmLargeFileAsync(string fileName, long sizeBytes) =>
        DialogService.LargeFileAsync(_owner, fileName, sizeBytes);
}
