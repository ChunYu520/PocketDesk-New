using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PocketDesk.Core;
using PocketDesk.Host.Services;

namespace PocketDesk.Host.ViewModels;

/// <summary>收纳项在宿主中的呈现模型（图标、选中状态等 UI 状态）。</summary>
public sealed class ItemViewModel : ObservableObject
{
    private Bitmap? _icon;
    private bool _isSelected;

    public ItemViewModel(BoxItem model)
    {
        Model = model;
    }

    public BoxItem Model { get; }

    public string Id => Model.Id;

    public string Name => Model.DisplayName;

    public string TypeHint => Model.TypeHint;

    public string Tooltip => Model.TooltipText;

    public bool IsMissing => Model.IsMissing;

    public bool IsFolder => Model.Kind == ItemKind.Folder;

    public string KindGlyph => Model.KindGlyph;

    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string RealPath => Model.RealPath;

    public async Task LoadIconAsync(IconLoader icons, double size)
    {
        if (Icon is not null || !Model.HasRealPath)
        {
            return;
        }

        var bitmap = await icons.LoadAsync(Model.RealPath, (int)Math.Round(size), IsFolder).ConfigureAwait(true);
        if (bitmap is not null)
        {
            Icon = bitmap;
        }
    }

    public void RefreshState()
    {
        BoxService.RefreshState(Model);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TypeHint));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(RealPath));
    }
}
