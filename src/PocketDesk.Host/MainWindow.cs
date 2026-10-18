using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Host.ViewModels;
using PocketDesk.Host.Views;
using PocketDesk.Shared;

namespace PocketDesk.Host;

/// <summary>
/// 桌面组件层。单个透明、无焦点窗口承载多张卡片：
/// 视觉始终活跃；只有点击输入控件时才临时获取系统键盘焦点。
/// </summary>
public sealed class MainWindow : Window
{
    private readonly HostServices _host;
    private readonly Canvas _canvas;
    private readonly Canvas _overlay;
    private readonly Dictionary<string, CardView> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Border _toast;
    private readonly TextBlock _toastText;
    private readonly DispatcherTimer _toastTimer;
    private WindowController? _controller;
    private CardViewModel? _activeCard;
    private bool _syncing;

    // 内部拖拽（跨卡片，不触发系统 OLE）
    private CardViewModel? _dragSource;
    private IReadOnlyList<ItemViewModel> _dragItems = Array.Empty<ItemViewModel>();
    private Border? _dragGhost;
    private CardViewModel? _dragTarget;
    private bool _externalDragStarted;

    public MainWindow(HostServices host)
    {
        _host = host;
        Title = "PocketDesk-New";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = true;

        _canvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        _overlay = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(_overlay);
        Content = root;

        _toastText = new TextBlock
        {
            FontSize = 12,
            Foreground = Solid(host.Theme.Palette.TextPrimary),
        };
        _toast = new Border
        {
            Background = Solid(host.Theme.Palette.MenuBackground),
            BorderBrush = Solid(host.Theme.Palette.MenuBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8),
            Child = _toastText,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 28),
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 8 24 0 #50000000")),
            IsHitTestVisible = false,
        };
        root.Children.Add(_toast);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.IsVisible = false;
        };

        _host.Theme.Changed += OnThemeChanged;
        _host.ViewModel.Cards.CollectionChanged += (_, _) => SyncCards();
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerCaptureLostEvent, (_, _) => CancelDrag(), RoutingStrategies.Tunnel, true);

        Opened += (_, _) => OnOpened();
        Closed += (_, _) =>
        {
            _controller?.Dispose();
            _host.Theme.Changed -= OnThemeChanged;
        };
    }

    public WindowController? Controller => _controller;

    public nint WindowHandle => _controller?.Handle ?? TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private void OnOpened()
    {
        var screen = Screens.Primary;
        if (screen is not null)
        {
            var work = screen.WorkingArea;
            var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
            Position = new PixelPoint(work.X, work.Y);
            Width = work.Width / scale;
            Height = work.Height / scale;
        }

        _controller = new WindowController(this, IsPointOverCard);
        _controller.Attach();
        _controller.InputStateChanged += () =>
        {
            if (!_controller.InputActive)
            {
                SetActiveCard(null);
            }
        };

        SyncCards();
        ApplyTheme();
        LogService.Info($"组件层窗口已创建：{Width:0}x{Height:0} DIP，屏幕缩放 {screen?.Scaling ?? 1:0.##}");
    }

    private void OnThemeChanged()
    {
        Dispatcher.UIThread.Post(ApplyTheme);
    }

    public void ApplyTheme()
    {
        var palette = _host.Theme.Palette;
        _toast.Background = Solid(palette.MenuBackground);
        _toast.BorderBrush = Solid(palette.MenuBorder);
        _toastText.Foreground = Solid(palette.TextPrimary);

        foreach (var view in _views.Values)
        {
            view.SetContent(view.ViewModel.Content);
        }
    }

    private bool IsPointOverCard(Point point)
    {
        foreach (var view in _views.Values)
        {
            if (view.IsVisible && view.Bounds.Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    public void SyncCards()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            var active = _host.ViewModel.Cards.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in _views.Keys.Where(id => !active.Contains(id)).ToList())
            {
                var view = _views[id];
                _canvas.Children.Remove(view);
                _views.Remove(id);
            }

            foreach (var card in _host.ViewModel.Cards)
            {
                if (!_views.TryGetValue(card.Id, out var view))
                {
                    view = new CardView(card, _host);
                    view.SetContent(card.Content);
                    _views[card.Id] = view;
                    _canvas.Children.Add(view);
                    card.PropertyChanged += (_, e) => OnCardPropertyChanged(card, e);
                }
                else if (view.ViewModel != card)
                {
                    // 实例被重新加载时替换视图。
                    _canvas.Children.Remove(view);
                    view = new CardView(card, _host);
                    view.SetContent(card.Content);
                    _views[card.Id] = view;
                    _canvas.Children.Add(view);
                }

                view.UpdateZIndex();
                UpdateCardBounds(card);
            }

            LogService.Info($"卡片视图已同步：{_views.Count} 张");
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnCardPropertyChanged(CardViewModel card, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CardViewModel.X) or nameof(CardViewModel.Y) or
            nameof(CardViewModel.Width) or nameof(CardViewModel.Height))
        {
            UpdateCardBounds(card);
        }
        else if (e.PropertyName is nameof(CardViewModel.ZOrder))
        {
            UpdateCardZOrder(card);
        }
        else if (e.PropertyName is nameof(CardViewModel.IsInputActive))
        {
            if (card.IsInputActive)
            {
                SetActiveCard(card);
            }
        }
    }

    public void UpdateCardBounds(CardViewModel card)
    {
        if (!_views.TryGetValue(card.Id, out var view))
        {
            return;
        }

        Canvas.SetLeft(view, card.X);
        Canvas.SetTop(view, card.Y);
        view.Width = card.Width;
        view.Height = card.Height;
    }

    public void UpdateCardZOrder(CardViewModel card)
    {
        if (_views.TryGetValue(card.Id, out var view))
        {
            view.ZIndex = card.ZOrder;
        }
    }

    public void RefreshCardContent(CardViewModel card)
    {
        if (_views.TryGetValue(card.Id, out var view))
        {
            view.SetContent(card.Content);
        }
    }

    public void RefreshCardChrome(CardViewModel? card)
    {
        if (card is not null && _views.TryGetValue(card.Id, out var view))
        {
            view.UpdateBounds();
        }
    }

    public void RequestInputFocus(CardViewModel? card, Control? target)
    {
        if (_controller is null)
        {
            target?.Focus();
            return;
        }

        if (card is not null)
        {
            SetActiveCard(card);
        }

        if (target is not null)
        {
            _controller.ActivateForInput(target);
        }
    }

    public void EndInputFocus(bool restoreForeground)
    {
        SetActiveCard(null);
        _controller?.DeactivateInput(restoreForeground);
    }

    private void SetActiveCard(CardViewModel? card)
    {
        if (_activeCard == card)
        {
            return;
        }

        if (_activeCard is not null)
        {
            _activeCard.IsInputActive = false;
        }

        _activeCard = card;
        if (_activeCard is not null)
        {
            _activeCard.IsInputActive = true;
        }
    }

    public void ShowToast(string message)
    {
        _toastText.Text = message;
        _toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void TileCards()
    {
        var cards = _host.ViewModel.Cards.ToList();
        if (cards.Count == 0)
        {
            return;
        }

        const double gap = 16;
        var columns = cards.Count <= 2 ? cards.Count : cards.Count <= 6 ? 3 : 4;
        var rows = (int)Math.Ceiling(cards.Count / (double)columns);
        var cellWidth = Math.Max(280, (Width - gap * (columns + 1)) / columns);
        var cellHeight = Math.Max(200, (Height - gap * (rows + 1)) / rows);

        for (var i = 0; i < cards.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            cards[i].SetBounds(
                gap + column * (cellWidth + gap),
                gap + row * (cellHeight + gap),
                cellWidth,
                cellHeight);
            UpdateCardBounds(cards[i]);
            cards[i].SavePlacement();
        }

        ShowToast("已按网格排列卡片");
    }

    public void BeginItemDrag(CardViewModel source, IReadOnlyList<ItemViewModel> items, PointerEventArgs e)
    {
        if (items.Count == 0 || _dragSource is not null)
        {
            return;
        }

        _dragSource = source;
        _dragItems = items;
        _externalDragStarted = false;
        if (source.IsInputActive)
        {
            EndInputFocus(restoreForeground: true);
        }

        _dragGhost = new Border
        {
            Background = Solid(_host.Theme.Palette.MenuBackground),
            BorderBrush = Solid(_host.Theme.Palette.Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 6),
            Child = new TextBlock
            {
                Text = items.Count == 1 ? items[0].Name : $"{items.Count} 个项目",
                FontSize = 12,
                Foreground = Solid(_host.Theme.Palette.TextPrimary),
            },
            IsHitTestVisible = false,
        };
        _overlay.Children.Add(_dragGhost);
        PositionGhost(e.GetPosition(this));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void PositionGhost(Point point)
    {
        if (_dragGhost is null)
        {
            return;
        }

        Canvas.SetLeft(_dragGhost, point.X + 14);
        Canvas.SetTop(_dragGhost, point.Y + 10);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        PositionGhost(point);

        var target = FindCardAt(point, _dragSource);
        if (!ReferenceEquals(target, _dragTarget))
        {
            if (_dragTarget is not null)
            {
                _dragTarget.IsDropTarget = false;
            }

            _dragTarget = target;
            if (_dragTarget is not null)
            {
                _dragTarget.IsDropTarget = true;
            }
        }

        var outside = point.X < 0 || point.Y < 0 || point.X > ClientSize.Width || point.Y > ClientSize.Height;
        if (outside && !_externalDragStarted && _dragSource.IsBox)
        {
            _externalDragStarted = true;
            _ = StartExternalDragAsync(e);
        }

        e.Handled = true;
    }

    private async Task StartExternalDragAsync(PointerEventArgs e)
    {
        var paths = _dragItems.Where(i => i.Model.HasRealPath && PathUtil.Exists(i.RealPath))
            .Select(i => i.RealPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        CancelDrag(keepSource: true);
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var transfer = new DataTransfer();
            foreach (var path in paths)
            {
                IStorageItem? item = Directory.Exists(path)
                    ? await StorageProvider.TryGetFolderFromPathAsync(path)
                    : await StorageProvider.TryGetFileFromPathAsync(path);
                if (item is not null)
                {
                    transfer.Add(DataTransferItem.CreateFile(item));
                }
            }

            if (transfer.Items.Count > 0)
            {
                await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy | DragDropEffects.Move);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"拖出到资源管理器失败：{ex.GetType().Name}: {ex.Message}");
            ShowToast("拖出失败；可右键使用“在资源管理器中显示”");
        }
        finally
        {
            _dragSource = null;
            _dragItems = Array.Empty<ItemViewModel>();
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragSource is null)
        {
            return;
        }

        var source = _dragSource;
        var target = _dragTarget;
        if (source.IsBox && target is { IsBox: true } && source.Box is not null && target.Box is not null &&
            !ReferenceEquals(source, target))
        {
            var result = BoxService.MoveItemsToBox(source.Box, target.Box, _dragItems.Select(i => i.Model).ToList());
            if (result.Moved > 0)
            {
                ShowToast($"已移动 {result.Moved} 项到「{target.Title}」");
                source.RefreshItemsOnly();
                target.RefreshItemsOnly();
            }
            else
            {
                ShowToast(result.Error ?? "没有项目被移动");
            }
        }
        else if (target is not null && !ReferenceEquals(source, target) && !target.IsBox)
        {
            ShowToast("该组件不支持接收文件");
        }
        else if (ReferenceEquals(source, target))
        {
            ShowToast("拖到另一个收纳盒以移动项目");
        }

        CancelDrag();
        e.Handled = true;
    }

    private CardViewModel? FindCardAt(Point point, CardViewModel? exclude)
    {
        CardViewModel? hit = null;
        foreach (var card in _host.ViewModel.Cards)
        {
            if (ReferenceEquals(card, exclude) || !_views.TryGetValue(card.Id, out var view))
            {
                continue;
            }

            if (view.Bounds.Contains(point) && (hit is null || card.ZOrder > hit.ZOrder))
            {
                hit = card;
            }
        }

        return hit;
    }

    private void CancelDrag(bool keepSource = false)
    {
        if (_dragTarget is not null)
        {
            _dragTarget.IsDropTarget = false;
            _dragTarget = null;
        }

        if (_dragGhost is not null)
        {
            _overlay.Children.Remove(_dragGhost);
            _dragGhost = null;
        }

        if (!keepSource)
        {
            _dragSource = null;
            _dragItems = Array.Empty<ItemViewModel>();
        }

        _externalDragStarted = false;
    }

    private static SolidColorBrush Solid(Color color) => new(color);
}
