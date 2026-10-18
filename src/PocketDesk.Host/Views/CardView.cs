using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Host.ViewModels;
using PocketDesk.Shared;

namespace PocketDesk.Host.Views;

/// <summary>卡片外壳：标题、输入焦点指示线、拖拽/缩放、右键菜单。内容由具体 Widget 提供。</summary>
public sealed class CardView : UserControl
{
    private readonly CardViewModel _card;
    private readonly HostServices _host;
    private readonly Border _cardBorder;
    private readonly Border _indicator;
    private readonly TextBlock _glyph;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly TextBox _titleEditor;
    private readonly TextBlock _count;
    private readonly Border _typeBadge;
    private readonly TextBlock _typeBadgeText;
    private readonly Button _menuButton;
    private readonly ContentControl _contentHost;
    private readonly Grid _header;
    private Point _dragStart;
    private Point _cardStart;
    private bool _pressed;
    private bool _dragging;
    private bool _resizing;
    private ResizeDirection _resizeDirection;
    private Size _resizeStartSize;
    private Point _resizeStartPoint;

    private enum ResizeDirection
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public CardView(CardViewModel card, HostServices host)
    {
        _card = card;
        _host = host;
        Focusable = true;

        _indicator = new Border
        {
            Height = 2,
            Background = Solid(host.Theme.Palette.Accent),
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(2, 2, 0, 0),
        };

        _glyph = new TextBlock
        {
            FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0),
        };

        _title = new TextBlock
        {
            FontSize = 13.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _subtitle = new TextBlock
        {
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _titleEditor = new TextBox
        {
            FontSize = 13.5,
            Padding = new Thickness(4, 2),
            MinHeight = 24,
            IsVisible = false,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
        };
        _titleEditor.KeyDown += OnTitleEditorKeyDown;
        _titleEditor.LostFocus += (_, _) => CommitRename();

        var titleStack = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(_title);
        Grid.SetRow(_subtitle, 1);
        titleStack.Children.Add(_subtitle);
        titleStack.Children.Add(_titleEditor);

        _count = new TextBlock
        {
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
            IsVisible = false,
        };

        _typeBadgeText = new TextBlock { FontSize = 9.5, FontWeight = FontWeight.Medium };
        _typeBadge = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            IsVisible = false,
            Child = _typeBadgeText,
        };

        _menuButton = new Button
        {
            Content = "\uE712",
            FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
            FontSize = 14,
            Width = 30,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Focusable = false,
        };
        _menuButton.Click += (_, _) => ShowMenu(_menuButton);

        _header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            Height = 40,
        };
        _header.Children.Add(_glyph);
        Grid.SetColumn(titleStack, 1);
        _header.Children.Add(titleStack);
        Grid.SetColumn(_count, 2);
        _header.Children.Add(_count);
        Grid.SetColumn(_typeBadge, 3);
        _header.Children.Add(_typeBadge);
        Grid.SetColumn(_menuButton, 4);
        _header.Children.Add(_menuButton);

        _contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
        };
        layout.Children.Add(_indicator);
        Grid.SetRow(_header, 1);
        layout.Children.Add(_header);
        Grid.SetRow(_contentHost, 2);
        layout.Children.Add(_contentHost);

        _cardBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Child = layout,
        };

        var root = new Panel { Background = Brushes.Transparent };
        root.Children.Add(_cardBorder);
        AddResizeGrips(root);

        Content = root;
        ContextMenu = BuildMenu();
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        _header.PointerPressed += OnHeaderPointerPressed;
        _header.PointerMoved += OnHeaderPointerMoved;
        _header.PointerReleased += OnHeaderPointerReleased;
        _header.PointerCaptureLost += (_, _) => EndDrag();
        _title.DoubleTapped += (_, e) =>
        {
            BeginRename();
            e.Handled = true;
        };
        _header.Cursor = new Cursor(StandardCursorType.SizeAll);

        _card.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CardViewModel.IsInputActive):
                case nameof(CardViewModel.IsDropTarget):
                case nameof(CardViewModel.CornerRadius):
                case nameof(CardViewModel.CardOpacity):
                    UpdateChrome();
                    break;
                case nameof(CardViewModel.Title):
                case nameof(CardViewModel.Subtitle):
                case nameof(CardViewModel.HeaderGlyph):
                case nameof(CardViewModel.CountText):
                case nameof(CardViewModel.TypeBadge):
                case nameof(CardViewModel.ShowTypeBadge):
                case nameof(CardViewModel.ShowCount):
                    UpdateChrome();
                    break;
            }
        };

        _host.Theme.Changed += OnThemeChanged;
        DetachedFromVisualTree += (_, _) => _host.Theme.Changed -= OnThemeChanged;

        UpdateChrome();
    }

    public CardViewModel ViewModel => _card;

    public void SetContent(Control? content)
    {
        _contentHost.Content = content;
        _contentHost.Margin = _card.IsBox ? new Thickness(10, 0, 10, 10) : new Thickness(0);
        _cardBorder.CornerRadius = new CornerRadius(_card.CornerRadius);
    }

    public void UpdateBounds()
    {
        // 由父级 Canvas 通过 Canvas.Left/Top + Width/Height 控制；此方法用于 DPI/尺寸变化后的重绘。
        InvalidateMeasure();
    }

    public void UpdateZIndex() => ZIndex = _card.ZOrder;

    public void BeginRename()
    {
        if (_titleEditor.IsVisible)
        {
            return;
        }

        _titleEditor.Text = _card.Title;
        _titleEditor.IsVisible = true;
        _title.IsVisible = false;
        _subtitle.IsVisible = false;
        _host.RequestInputFocus(_card, _titleEditor);
        Dispatcher.UIThread.Post(() =>
        {
            _titleEditor.Focus();
            _titleEditor.SelectAll();
        });
    }

    private void OnTitleEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void CommitRename()
    {
        if (!_titleEditor.IsVisible)
        {
            return;
        }

        var name = _titleEditor.Text?.Trim();
        _titleEditor.IsVisible = false;
        _title.IsVisible = true;
        _subtitle.IsVisible = true;
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, _card.Title, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (_card.IsBox && _card.Box is not null)
            {
                BoxService.RenameBox(_card.Box, name);
                _card.RefreshFromModel();
            }
            else
            {
                _card.Instance.DisplayName = name;
                WidgetRepository.Update(_card.Instance);
                _card.Instance.DisplayName = name;
                _host.ViewModel.RefreshCard(_card);
            }

            _host.Window?.RefreshCardChrome(_card);
        }
        catch (Exception ex)
        {
            _host.ShowToast($"重命名失败：{ex.Message}");
        }
    }

    private void CancelRename()
    {
        _titleEditor.IsVisible = false;
        _title.IsVisible = true;
        _subtitle.IsVisible = true;
    }

    private void UpdateChrome()
    {
        var palette = _host.Theme.Palette;
        var radius = new CornerRadius(_card.CornerRadius);
        _cardBorder.CornerRadius = radius;
        _indicator.CornerRadius = new CornerRadius(radius.TopLeft, radius.TopRight, 0, 0);
        _indicator.IsVisible = _card.IsInputActive && AppSettingsService.Current.ShowFocusIndicator;
        _indicator.Background = Solid(palette.Accent);

        var background = _card.IsBox
            ? Color.FromArgb((byte)Math.Clamp((int)Math.Round(_card.CardOpacity * 255), 40, 255),
                palette.CardBackground.R, palette.CardBackground.G, palette.CardBackground.B)
            : palette.CardBackground;
        _cardBorder.Background = Solid(background);

        var borderBase = palette.IsDark ? Colors.White : Colors.Black;
        var borderAlpha = _card.IsBox && _card.Box is not null
            ? _card.Box.Appearance.BorderOpacity
            : AppSettingsService.Current.CardBorderOpacity;
        _cardBorder.BorderBrush = _card.IsDropTarget
            ? Solid(palette.Accent)
            : Solid(Color.FromArgb((byte)Math.Clamp((int)Math.Round(borderAlpha * 255), 0, 255), borderBase.R, borderBase.G, borderBase.B));
        _cardBorder.BorderThickness = new Thickness(_card.IsDropTarget ? 2 : 1);

        var shadowAlpha = (byte)Math.Clamp((int)Math.Round(AppSettingsService.Current.ShadowStrength / 100 * 110), 0, 120);
        _cardBorder.BoxShadow = shadowAlpha == 0
            ? default
            : new BoxShadows(BoxShadow.Parse(
                $"0 10 30 0 #{(shadowAlpha):X2}{palette.Shadow.R:X2}{palette.Shadow.G:X2}{palette.Shadow.B:X2}"));

        _glyph.Text = _card.HeaderGlyph;
        _glyph.Foreground = Solid(palette.TextSecondary);
        _title.Text = _card.Title;
        _title.Foreground = Solid(palette.TextPrimary);
        _subtitle.Text = _card.Subtitle;
        _subtitle.Foreground = Solid(palette.TextMuted);
        _count.Text = _card.CountText;
        _count.Foreground = Solid(palette.TextMuted);
        _count.IsVisible = _card.ShowCount;
        _typeBadge.IsVisible = _card.ShowTypeBadge;
        _typeBadge.Background = Solid(Color.FromArgb(38, palette.Accent.R, palette.Accent.G, palette.Accent.B));
        _typeBadgeText.Text = _card.TypeBadge;
        _typeBadgeText.Foreground = Solid(palette.Accent);
        _menuButton.Foreground = Solid(palette.TextSecondary);

        _titleEditor.Background = Solid(palette.InputBackground);
        _titleEditor.Foreground = Solid(palette.TextPrimary);
        _titleEditor.BorderBrush = Solid(palette.Accent);

        _header.Background = Brushes.Transparent;
        ContextMenu = BuildMenu();
    }

    private void OnThemeChanged() => Dispatcher.UIThread.Post(UpdateChrome);

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is TextBox textBox)
        {
            _host.RequestInputFocus(_card, textBox);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _card.IsInputActive)
        {
            _host.Window?.EndInputFocus(restoreForeground: true);
            e.Handled = true;
        }
    }

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        // 点击卡片任意位置都把卡片提到最前，但不改变其他卡片的视觉状态。
        _host.ViewModel.BringToFront(_card);

        if (_card.IsInputActive && e.Source is not TextBox && e.Source is not Button)
        {
            _host.Window?.EndInputFocus(restoreForeground: true);
        }

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            ShowMenu(this);
            e.Handled = true;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_header).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Button || e.Source is TextBox)
        {
            return;
        }

        _pressed = true;
        _dragging = false;
        _dragStart = e.GetPosition(this);
        _cardStart = new Point(_card.X, _card.Y);
        // 不在按下时立即捕获指针，否则会吞掉标题的双击重命名手势；超过阈值再捕获。
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        if (!_dragging && Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(_header);
        var maxX = Math.Max(0, (TopLevel.GetTopLevel(this)?.ClientSize.Width ?? 1200) - _card.Width - 8);
        var maxY = Math.Max(0, (TopLevel.GetTopLevel(this)?.ClientSize.Height ?? 700) - _card.Height - 8);
        _card.X = Math.Clamp(_cardStart.X + dx, 8, maxX);
        _card.Y = Math.Clamp(_cardStart.Y + dy, 8, maxY);
        _host.Window?.UpdateCardBounds(_card);
        e.Handled = true;
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _card.SavePlacement();
            _host.ShowToast($"「{_card.Title}」位置已保存");
        }

        EndDrag();
    }

    private void EndDrag()
    {
        _pressed = false;
        _dragging = false;
    }

    private void AddResizeGrips(Panel root)
    {
        AddGrip(root, ResizeDirection.Top, HorizontalAlignment.Stretch, VerticalAlignment.Top, new Thickness(10, 0, 10, 0), 5, StandardCursorType.SizeNorthSouth);
        AddGrip(root, ResizeDirection.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, new Thickness(10, 0, 10, 0), 5, StandardCursorType.SizeNorthSouth);
        AddGrip(root, ResizeDirection.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch, new Thickness(0, 10, 0, 10), 5, StandardCursorType.SizeWestEast);
        AddGrip(root, ResizeDirection.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch, new Thickness(0, 10, 0, 10), 5, StandardCursorType.SizeWestEast);
        AddGrip(root, ResizeDirection.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(0), 12, StandardCursorType.TopLeftCorner);
        AddGrip(root, ResizeDirection.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0), 12, StandardCursorType.TopRightCorner);
        AddGrip(root, ResizeDirection.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(0), 12, StandardCursorType.BottomLeftCorner);
        AddGrip(root, ResizeDirection.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0), 12, StandardCursorType.BottomRightCorner);
    }

    private void AddGrip(
        Panel root,
        ResizeDirection direction,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        Thickness margin,
        double thickness,
        StandardCursorType cursor)
    {
        var isVerticalEdge = direction is ResizeDirection.Top or ResizeDirection.Bottom;
        var isHorizontalEdge = direction is ResizeDirection.Left or ResizeDirection.Right;
        var grip = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Margin = margin,
            Width = isVerticalEdge ? double.NaN : thickness,
            Height = isHorizontalEdge ? double.NaN : thickness,
            Cursor = new Cursor(cursor),
        };

        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _resizing = true;
            _resizeDirection = direction;
            _resizeStartPoint = e.GetPosition(this);
            _resizeStartSize = new Size(_card.Width, _card.Height);
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!_resizing || _resizeDirection != direction)
            {
                return;
            }

            var point = e.GetPosition(this);
            var dx = point.X - _resizeStartPoint.X;
            var dy = point.Y - _resizeStartPoint.Y;
            var width = _resizeStartSize.Width;
            var height = _resizeStartSize.Height;
            var x = _card.X;
            var y = _card.Y;

            if (direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
            {
                width += dx;
            }
            else if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            {
                width -= dx;
                x += dx;
            }

            if (direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
            {
                height += dy;
            }
            else if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            {
                height -= dy;
                y += dy;
            }

            var newWidth = Math.Clamp(width, 280, 1600);
            var newHeight = Math.Clamp(height, 200, 1200);
            if (newWidth != width)
            {
                x = _card.X;
            }

            if (newHeight != height)
            {
                y = _card.Y;
            }

            _card.SetBounds(Math.Max(4, x), Math.Max(4, y), newWidth, newHeight);
            _host.Window?.UpdateCardBounds(_card);
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) =>
        {
            if (_resizing)
            {
                _resizing = false;
                _resizeDirection = ResizeDirection.None;
                _card.SavePlacement();
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        };

        root.Children.Add(grip);
    }

    private void ShowMenu(Control anchor)
    {
        var menu = BuildMenu();
        menu.Open(anchor);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("重命名", BeginRename));
        menu.Items.Add(MenuItem("置于最前", () => _host.ViewModel.BringToFront(_card)));

        if (_card.IsBox && _card.Box is not null)
        {
            var view = new MenuItem { Header = "视图" };
            view.Items.Add(MenuItem((_card.IsGrid ? "✓ " : string.Empty) + "网格", () => SetView("grid")));
            view.Items.Add(MenuItem((!_card.IsGrid ? "✓ " : string.Empty) + "列表", () => SetView("list")));
            menu.Items.Add(view);

            var sort = new MenuItem { Header = "排序" };
            sort.Items.Add(SortItem("name", "名称"));
            sort.Items.Add(SortItem("type", "类型"));
            sort.Items.Add(SortItem("modified", "修改时间"));
            sort.Items.Add(SortItem("added", "添加时间"));
            sort.Items.Add(SortItem("custom", "自定义"));
            menu.Items.Add(sort);

            var size = new MenuItem { Header = "卡片尺寸" };
            size.Items.Add(MenuItem("紧凑", () => ResizeTo(320, 300)));
            size.Items.Add(MenuItem("标准", () => ResizeTo(380, 360)));
            size.Items.Add(MenuItem("宽", () => ResizeTo(520, 400)));
            menu.Items.Add(size);

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("新建收纳盒", () => _host.ViewModel.AddBox(null, BoxType.Normal, null)));
            menu.Items.Add(MenuItem("打开所在位置", () =>
            {
                var path = _card.Box.Type == BoxType.Target && !string.IsNullOrEmpty(_card.Box.TargetPath)
                    ? _card.Box.TargetPath
                    : AppPaths.BoxStorage(_card.Box.Id);
                Directory.CreateDirectory(path);
                ShellService.Open(path);
            }));
            menu.Items.Add(MenuItem("组件设置", () => _host.OpenSettings()));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("删除收纳盒", DeleteBox));
        }
        else
        {
            menu.Items.Add(MenuItem("组件设置", () => _host.OpenSettings()));
            menu.Items.Add(MenuItem("打开设置界面", () => _host.OpenSettings()));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("移除组件", RemoveWidget));
        }

        return menu;
    }

    private void SetView(string view)
    {
        _card.UpdateBoxAppearance(a => a.View = view);
        _host.Window?.RefreshCardContent(_card);
    }

    private MenuItem SortItem(string field, string label)
    {
        var current = _card.Box?.SortField ?? "name";
        return MenuItem((string.Equals(current, field, StringComparison.OrdinalIgnoreCase) ? "✓ " : string.Empty) + label, () =>
        {
            if (_card.Box is null)
            {
                return;
            }

            BoxService.SetSort(_card.Box, field, _card.Box.SortDescending);
            _card.RefreshFromModel();
        });
    }

    private void ResizeTo(double width, double height)
    {
        _card.SetBounds(_card.X, _card.Y, width, height);
        _card.SavePlacement();
        _host.Window?.UpdateCardBounds(_card);
    }

    private async void DeleteBox()
    {
        if (_card.Box is null || _host.Window is null)
        {
            return;
        }

        if (AppSettingsService.Current.ConfirmDelete &&
            !await DialogService.ConfirmAsync(
                _host.Window,
                "删除收纳盒",
                $"确定删除「{_card.Title}」？\n普通收纳盒中的文件会先还原到原位置；映射/目标盒只移除引用。"))
        {
            return;
        }

        try
        {
            _host.ViewModel.RemoveCard(_card, deleteFiles: true);
            _host.ShowToast($"已删除「{_card.Title}」");
        }
        catch (Exception ex)
        {
            _host.ShowToast($"删除失败：{ex.Message}");
        }
    }

    private async void RemoveWidget()
    {
        if (_host.Window is null)
        {
            return;
        }

        if (!await DialogService.ConfirmAsync(_host.Window, "移除组件", $"确定移除「{_card.Title}」？"))
        {
            return;
        }

        WidgetRepository.Delete(_card.Instance.Id);
        _host.ViewModel.Cards.Remove(_card);
        _host.Window.SyncCards();
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static SolidColorBrush Solid(Color color) => new(color);
}
