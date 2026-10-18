using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Host.ViewModels;
using PocketDesk.Shared;

namespace PocketDesk.Host.Views;

/// <summary>
/// 卡片外壳：Win11 Fluent 风格的圆角卡片、标题、输入焦点指示线、拖拽/缩放与操作按钮。
/// 内容由具体 Widget 提供。
/// </summary>
public sealed class CardView : UserControl
{
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons,Segoe MDL2 Assets");

    private readonly CardViewModel _card;
    private readonly HostServices _host;
    private readonly Border _cardBorder;
    private readonly Border _indicator;
    private readonly Border _glyphBadge;
    private readonly TextBlock _glyph;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly TextBox _titleEditor;
    private readonly Border _countPill;
    private readonly TextBlock _countText;
    private readonly Button _settingsButton;
    private readonly Button _menuButton;
    private readonly Button _viewButton;
    private readonly Button _openFolderButton;
    private readonly TextBlock _footerHint;
    private readonly Grid _footer;
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
        FontFamily = UiFont;

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
            FontFamily = IconFont,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _glyphBadge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 10, 0),
            Child = _glyph,
        };

        _title = new TextBlock
        {
            FontFamily = UiFont,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _subtitle = new TextBlock
        {
            FontFamily = UiFont,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        };

        _titleEditor = new TextBox
        {
            FontFamily = UiFont,
            FontSize = 13,
            Padding = new Thickness(6, 3),
            MinHeight = 26,
            IsVisible = false,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
        };
        _titleEditor.KeyDown += OnTitleEditorKeyDown;
        _titleEditor.LostFocus += (_, _) => CommitRename();

        var titleStack = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleStack.Children.Add(_title);
        Grid.SetRow(_subtitle, 1);
        titleStack.Children.Add(_subtitle);
        titleStack.Children.Add(_titleEditor);

        _countText = new TextBlock
        {
            FontFamily = UiFont,
            FontSize = 10.5,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _countPill = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            IsVisible = false,
            Child = _countText,
        };

        _settingsButton = ChromeButton("\uE713", "设置", () => _host.OpenSettings());
        Button? menuButton = null;
        menuButton = ChromeButton("\uE712", "更多操作", () =>
        {
            if (menuButton is not null)
            {
                ShowMenu(menuButton);
            }
        });
        _menuButton = menuButton;

        _header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            Height = 52,
            Background = Brushes.Transparent,
        };
        _header.Children.Add(_glyphBadge);
        Grid.SetColumn(titleStack, 1);
        _header.Children.Add(titleStack);
        Grid.SetColumn(_countPill, 2);
        _header.Children.Add(_countPill);
        Grid.SetColumn(_settingsButton, 3);
        _header.Children.Add(_settingsButton);
        Grid.SetColumn(_menuButton, 4);
        _header.Children.Add(_menuButton);

        _contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        _footerHint = new TextBlock
        {
            FontFamily = UiFont,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _viewButton = ChromeButton("\uE8FD", "切换视图", ToggleView);
        _openFolderButton = ChromeButton("\uE8B7", "打开文件夹", OpenFolder);
        _footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Height = 34,
            Margin = new Thickness(14, 0, 10, 6),
            IsVisible = false,
        };
        _footer.Children.Add(_footerHint);
        Grid.SetColumn(_viewButton, 1);
        _footer.Children.Add(_viewButton);
        Grid.SetColumn(_openFolderButton, 2);
        _footer.Children.Add(_openFolderButton);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
        };
        layout.Children.Add(_indicator);
        Grid.SetRow(_header, 1);
        layout.Children.Add(_header);
        Grid.SetRow(_contentHost, 2);
        layout.Children.Add(_contentHost);
        Grid.SetRow(_footer, 3);
        layout.Children.Add(_footer);

        _cardBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
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
        _header.DoubleTapped += OnHeaderDoubleTapped;
        _header.Cursor = new Cursor(StandardCursorType.SizeAll);

        _card.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CardViewModel.IsInputActive):
                case nameof(CardViewModel.IsDropTarget):
                case nameof(CardViewModel.CornerRadius):
                case nameof(CardViewModel.CardOpacity):
                case nameof(CardViewModel.Title):
                case nameof(CardViewModel.Subtitle):
                case nameof(CardViewModel.HeaderGlyph):
                case nameof(CardViewModel.CountText):
                case nameof(CardViewModel.ShowCount):
                case nameof(CardViewModel.IsGrid):
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
        _contentHost.Margin = _card.IsBox ? new Thickness(12, 0, 12, 0) : new Thickness(0);
        _cardBorder.CornerRadius = new CornerRadius(Math.Clamp(_card.CornerRadius, 8, 28));
    }

    public void UpdateBounds() => InvalidateMeasure();

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
        var radius = new CornerRadius(Math.Clamp(_card.CornerRadius, 8, 28));
        _cardBorder.CornerRadius = radius;
        _indicator.CornerRadius = new CornerRadius(radius.TopLeft, radius.TopRight, 0, 0);
        _indicator.IsVisible = _card.IsInputActive && AppSettingsService.Current.ShowFocusIndicator;
        _indicator.Background = Solid(palette.Accent);

        var baseColor = _card.IsBox
            ? Color.FromArgb((byte)Math.Clamp((int)Math.Round(_card.CardOpacity * 255), 40, 255),
                palette.CardBackground.R, palette.CardBackground.G, palette.CardBackground.B)
            : palette.CardBackground;
        var bottom = Color.FromArgb(
            baseColor.A,
            (byte)Math.Max(0, baseColor.R - (palette.IsDark ? 9 : 4)),
            (byte)Math.Max(0, baseColor.G - (palette.IsDark ? 8 : 4)),
            (byte)Math.Max(0, baseColor.B - (palette.IsDark ? 6 : 3)));
        _cardBorder.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(baseColor, 0),
                new GradientStop(bottom, 1),
            },
        };

        var borderBase = palette.IsDark ? Colors.White : Colors.Black;
        var borderAlpha = _card.IsBox && _card.Box is not null
            ? _card.Box.Appearance.BorderOpacity
            : AppSettingsService.Current.CardBorderOpacity;
        _cardBorder.BorderBrush = _card.IsDropTarget
            ? Solid(palette.Accent)
            : Solid(Color.FromArgb((byte)Math.Clamp((int)Math.Round(borderAlpha * 255), 0, 255),
                borderBase.R, borderBase.G, borderBase.B));
        _cardBorder.BorderThickness = new Thickness(_card.IsDropTarget ? 2 : 1);

        var shadowAlpha = (byte)Math.Clamp((int)Math.Round(AppSettingsService.Current.ShadowStrength / 100 * 120), 0, 130);
        _cardBorder.BoxShadow = shadowAlpha == 0
            ? default
            : new BoxShadows(BoxShadow.Parse(
                $"0 12 34 0 #{(shadowAlpha):X2}{palette.Shadow.R:X2}{palette.Shadow.G:X2}{palette.Shadow.B:X2}"));

        _glyph.Text = _card.HeaderGlyph;
        _glyph.Foreground = Solid(palette.Accent);
        _glyphBadge.Background = Solid(Color.FromArgb(42, palette.Accent.R, palette.Accent.G, palette.Accent.B));
        _title.Text = _card.Title;
        _title.Foreground = Solid(palette.TextPrimary);
        _subtitle.Text = _card.Subtitle;
        _subtitle.Foreground = Solid(palette.TextMuted);
        _countText.Text = _card.CountText;
        _countText.Foreground = Solid(palette.TextSecondary);
        _countPill.Background = Solid(palette.Hover);
        _countPill.IsVisible = _card.ShowCount && _card.IsBox;
        _settingsButton.Foreground = Solid(palette.TextSecondary);
        _menuButton.Foreground = Solid(palette.TextSecondary);
        _viewButton.Foreground = Solid(palette.TextSecondary);
        _openFolderButton.Foreground = Solid(palette.TextSecondary);
        _footerHint.Foreground = Solid(palette.TextMuted);
        _footer.IsVisible = _card.IsBox;
        _footerHint.Text = BuildFooterHint();
        _viewButton.Content = _card.IsGrid ? "\uE8FD" : "\uE80A";
        ToolTip.SetTip(_viewButton, _card.IsGrid ? "切换为列表" : "切换为网格");

        _titleEditor.Background = Solid(palette.InputBackground);
        _titleEditor.Foreground = Solid(palette.TextPrimary);
        _titleEditor.BorderBrush = Solid(palette.Accent);
        ContextMenu = BuildMenu();
    }

    private string BuildFooterHint()
    {
        if (!_card.IsBox)
        {
            return string.Empty;
        }

        if (_card.Items.Count == 0)
        {
            return "拖入文件即可收纳";
        }

        var sort = _card.Box?.SortField?.ToLowerInvariant() switch
        {
            "type" => "类型排序",
            "modified" => "修改时间",
            "added" => "添加时间",
            "custom" => "自定义排序",
            _ => "名称排序",
        };
        return $"{sort} · {(_card.IsGrid ? "网格" : "列表")}";
    }

    private Button ChromeButton(string glyph, string tooltip, Action click)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = IconFont,
            FontSize = 14,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => click();
        button.PointerEntered += (_, _) => button.Background = Solid(_host.Theme.Palette.Hover);
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.AddHandler(
            PointerPressedEvent,
            (_, _) => button.Background = Solid(Color.FromArgb(38,
                _host.Theme.Palette.Accent.R, _host.Theme.Palette.Accent.G, _host.Theme.Palette.Accent.B)),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        button.AddHandler(
            PointerReleasedEvent,
            (_, _) => button.Background = Solid(_host.Theme.Palette.Hover),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        return button;
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
        _host.ViewModel.BringToFront(_card);

        if (_card.IsInputActive && !IsInteractiveSource(e.Source))
        {
            _host.Window?.EndInputFocus(restoreForeground: true);
        }
    }

    private bool IsInteractiveSource(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != _header; visual = visual.GetVisualParent())
        {
            if (visual is Button || visual is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_header).Properties.IsLeftButtonPressed || IsInteractiveSource(e.Source))
        {
            return;
        }

        _pressed = true;
        _dragging = false;
        _dragStart = WindowPoint(e);
        _cardStart = new Point(_card.X, _card.Y);
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var point = WindowPoint(e);
        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        if (!_dragging && Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(_header);
        var topLevel = TopLevel.GetTopLevel(this);
        var maxX = Math.Max(0, (topLevel?.ClientSize.Width ?? 1200) - _card.Width - 8);
        var maxY = Math.Max(0, (topLevel?.ClientSize.Height ?? 700) - _card.Height - 8);
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
        }

        EndDrag();
    }

    private void OnHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsInteractiveSource(e.Source))
        {
            return;
        }

        BeginRename();
        e.Handled = true;
    }

    private void EndDrag()
    {
        _pressed = false;
        _dragging = false;
    }

    private Point WindowPoint(PointerEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        return topLevel is null ? e.GetPosition(this) : e.GetPosition(topLevel);
    }

    private void ToggleView()
    {
        if (!_card.IsBox || _card.Box is null)
        {
            return;
        }

        _card.UpdateBoxAppearance(a => a.View = _card.IsGrid ? "list" : "grid");
        _host.Window?.RefreshCardContent(_card);
        UpdateChrome();
    }

    private void OpenFolder()
    {
        if (_card.Box is null)
        {
            return;
        }

        var path = _card.Box.Type == BoxType.Target && !string.IsNullOrEmpty(_card.Box.TargetPath)
            ? _card.Box.TargetPath
            : AppPaths.BoxStorage(_card.Box.Id);
        Directory.CreateDirectory(path);
        ShellService.Open(path);
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
            _resizeStartPoint = WindowPoint(e);
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

            var point = WindowPoint(e);
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
        menu.Items.Add(MenuItem("打开设置", () => _host.OpenSettings()));

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
            size.Items.Add(MenuItem("标准", () => ResizeTo(380, 380)));
            size.Items.Add(MenuItem("宽", () => ResizeTo(520, 420)));
            menu.Items.Add(size);

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("新建收纳盒", () => _host.ViewModel.AddBox(null, BoxType.Normal, null)));
            menu.Items.Add(MenuItem("打开所在位置", OpenFolder));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("删除收纳盒", DeleteBox));
        }
        else
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("移除组件", RemoveWidget));
        }

        return menu;
    }

    private void SetView(string view)
    {
        _card.UpdateBoxAppearance(a => a.View = view);
        _host.Window?.RefreshCardContent(_card);
        UpdateChrome();
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
