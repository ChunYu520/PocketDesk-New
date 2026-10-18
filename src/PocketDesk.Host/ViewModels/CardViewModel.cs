using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets;

namespace PocketDesk.Host.ViewModels;

public enum CardKind
{
    Box,
    Widget,
}

/// <summary>
/// 一张卡片（收纳盒或通用组件实例）。位置/尺寸使用组件层窗口内的 DIP 坐标。
/// 卡片视觉状态完全由自身属性驱动，不依赖系统窗口焦点。
/// </summary>
public sealed class CardViewModel : ObservableObject
{
    private readonly HostServices _host;
    private string _searchText = string.Empty;
    private bool _isInputActive;
    private bool _isDropTarget;
    private double _x;
    private double _y;
    private double _width = 380;
    private double _height = 360;
    private int _zOrder;

    public CardViewModel(Box box, IWidget widget, HostServices host)
    {
        Kind = CardKind.Box;
        Box = box;
        Widget = widget;
        _host = host;
        var placement = box.Placement ?? BoxService.DefaultPlacement(box.ZOrder);
        _x = placement.X;
        _y = placement.Y;
        _width = placement.Width;
        _height = placement.Height;
        _zOrder = box.ZOrder;

        Instance = new WidgetInstance
        {
            Id = box.Id,
            WidgetTypeId = widget.Id,
            DisplayName = box.Name,
            IsVisible = box.IsVisible,
            ZOrder = box.ZOrder,
            X = _x,
            Y = _y,
            Width = _width,
            Height = _height,
            Settings = BoxSettings(box),
            CreatedAt = box.CreatedAt,
        };
    }

    public CardViewModel(WidgetInstance instance, IWidget widget, HostServices host)
    {
        Kind = CardKind.Widget;
        Instance = instance;
        Widget = widget;
        _host = host;
        _x = instance.X;
        _y = instance.Y;
        _width = instance.Width;
        _height = instance.Height;
        _zOrder = instance.ZOrder;
    }

    public CardKind Kind { get; }

    public Box? Box { get; private set; }

    public WidgetInstance Instance { get; private set; }

    public IWidget Widget { get; }

    public IWidgetContext? Context { get; set; }

    public Control? Content { get; set; }

    public ObservableCollection<ItemViewModel> Items { get; } = new();

    public ObservableCollection<ItemViewModel> VisibleItems { get; } = new();

    public string Id => Instance.Id;

    public bool IsBox => Kind == CardKind.Box;

    public string Title => IsBox ? Box?.Name ?? "收纳盒" : Instance.DisplayName;

    public string Subtitle => IsBox
        ? $"{Box!.TypeShortLabel} · {Items.Count} 项"
        : $"组件 · {_host.Widgets.Get(Instance.WidgetTypeId)?.DisplayName ?? "未知"}";

    public string HeaderGlyph => IsBox ? Box!.TypeGlyph : Widget.Glyph;

    public bool ShowTypeBadge => IsBox && Box!.Type != BoxType.Normal;

    public string TypeBadge => IsBox ? Box!.TypeShortLabel : string.Empty;

    public string CountText => IsBox ? $"{Items.Count} 项" : string.Empty;

    public bool ShowCount => IsBox && (Box?.Appearance.ShowItemCount ?? AppSettingsService.Current.ShowItemCount);

    public bool IsEmpty => IsBox && Items.Count == 0;

    public bool HasNoSearchResult => IsBox && Items.Count > 0 && VisibleItems.Count == 0;

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, Math.Clamp(value, 260, 1600));
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, Math.Clamp(value, 180, 1200));
    }

    public int ZOrder
    {
        get => _zOrder;
        set
        {
            if (SetProperty(ref _zOrder, value))
            {
                if (IsBox && Box is not null)
                {
                    Box.ZOrder = value;
                }
                else
                {
                    Instance.ZOrder = value;
                }
            }
        }
    }

    public bool IsInputActive
    {
        get => _isInputActive;
        set => SetProperty(ref _isInputActive, value);
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    public bool IsDragging { get; set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public double CornerRadius => IsBox
        ? Box!.Appearance.CornerRadius
        : Math.Max(8, AppSettingsService.Current.DefaultCornerRadius);

    public double CardOpacity => IsBox
        ? Box!.Appearance.BackgroundOpacity
        : AppSettingsService.Current.CardOpacity;

    public double IconSize => IsBox
        ? Math.Clamp(Box!.Appearance.IconSize, 24, 128)
        : 48;

    public int NameLines => IsBox
        ? Math.Clamp(Box!.Appearance.NameLines, 0, 3)
        : 2;

    public bool IsGrid => !IsBox || Box!.Appearance.ViewMode == BoxViewMode.Grid;

    public int Columns => IsBox ? Box!.Appearance.Columns : 0;

    public bool IsVisible
    {
        get => IsBox ? Box!.IsVisible : Instance.IsVisible;
        set
        {
            if (IsBox && Box is not null)
            {
                BoxService.SetVisible(Box, value);
            }
            else
            {
                Instance.IsVisible = value;
                WidgetRepository.SetVisible(Instance.Id, value);
            }

            OnPropertyChanged();
        }
    }

    private static Dictionary<string, string> BoxSettings(Box box) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["view"] = box.Appearance.View,
        ["showItemCount"] = box.Appearance.ShowItemCount ? "true" : "false",
        ["sortField"] = box.SortField,
    };

    public void Initialize()
    {
        if (IsBox)
        {
            ReloadItems();
        }

        ApplyFilter();
    }

    public void ReloadItems()
    {
        if (!IsBox || Box is null)
        {
            return;
        }

        var selected = Items.Where(i => i.IsSelected).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var models = BoxService.GetItems(Box);
        Items.Clear();
        foreach (var model in models)
        {
            var vm = new ItemViewModel(model) { IsSelected = selected.Contains(model.Id) };
            Items.Add(vm);
            _ = vm.LoadIconAsync(_host.Icons, IconSize);
        }

        ApplyFilter();
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowCount));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void ApplyFilter()
    {
        var query = _searchText.Trim();
        VisibleItems.Clear();
        foreach (var item in Items)
        {
            if (query.Length == 0 ||
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.TypeHint.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                VisibleItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasNoSearchResult));
    }

    /// <summary>从数据库重新读取名称、外观与设置，供设置界面/其他卡片操作后刷新。</summary>
    public void RefreshFromModel()
    {
        if (IsBox && Box is not null)
        {
            var fresh = BoxRepository.Get(Box.Id);
            if (fresh is null)
            {
                return;
            }

            Box = fresh;
            Instance.DisplayName = fresh.Name;
            if (fresh.Placement is { } placement)
            {
                X = placement.X;
                Y = placement.Y;
                Width = placement.Width;
                Height = placement.Height;
            }

            ReloadItems();
        }
        else
        {
            var fresh = WidgetRepository.Get(Instance.Id);
            if (fresh is not null)
            {
                Instance.DisplayName = fresh.DisplayName;
                Instance.Settings = fresh.Settings;
            }
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HeaderGlyph));
        OnPropertyChanged(nameof(TypeBadge));
        OnPropertyChanged(nameof(ShowTypeBadge));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowCount));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(NameLines));
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void RefreshItemsOnly()
    {
        if (!IsBox)
        {
            return;
        }

        ReloadItems();
    }

    public void ApplyAppearance(double? defaultCornerRadius = null)
    {
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(NameLines));
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(ShowCount));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HeaderGlyph));
    }

    public void SavePlacement()
    {
        if (IsBox && Box is not null)
        {
            Box.Placement = new WindowPlacement
            {
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                State = "Normal",
            };
            BoxService.SavePlacement(Box, Box.Placement);
        }
        else
        {
            Instance.X = X;
            Instance.Y = Y;
            Instance.Width = Width;
            Instance.Height = Height;
            WidgetRepository.Update(Instance);
        }
    }

    public void SetBounds(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public void NotifyWidgetSettingsChanged() => (Context as WidgetContext)?.RaiseSettingsChanged();

    public void UpdateBoxAppearance(Action<BoxAppearance> mutate)
    {
        if (!IsBox || Box is null)
        {
            return;
        }

        var appearance = Box.Appearance.Clone();
        mutate(appearance);
        BoxService.SaveAppearance(Box, appearance);
        RefreshFromModel();
    }
}
