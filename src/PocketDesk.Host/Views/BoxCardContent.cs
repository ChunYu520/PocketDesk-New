using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Host.ViewModels;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets;

namespace PocketDesk.Host.Views;

/// <summary>收纳盒卡片内容：搜索、图标网格/列表、拖入拖出、右键菜单。</summary>
public sealed class BoxCardContent : UserControl
{
    private readonly Box _box;
    private readonly IWidgetContext _context;
    private readonly HostServices _host;
    private readonly CardViewModel _card;
    private readonly ListBox _list;
    private readonly TextBox _search;
    private readonly Border _dropOverlay;
    private readonly TextBlock _emptyHint;
    private readonly TextBlock _noResultHint;
    private readonly Grid _root;
    private bool _subscribed;

    public BoxCardContent(Box box, IWidgetContext context, HostServices host)
    {
        _box = box;
        _context = context;
        _host = host;
        _card = host.ViewModel.Cards.FirstOrDefault(c => c.Id == box.Id)
                ?? throw new InvalidOperationException("收纳盒卡片尚未创建。");

        _search = new TextBox
        {
            Watermark = "搜索",
            Height = 34,
            FontFamily = new FontFamily("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI"),
            FontSize = 12.5,
            Padding = new Thickness(32, 5, 10, 5),
            Background = Solid(host.Theme.Palette.InputBackground),
            Foreground = Solid(host.Theme.Palette.TextPrimary),
            BorderBrush = Solid(host.Theme.Palette.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _search.TextChanged += (_, _) => _card.SearchText = _search.Text ?? string.Empty;
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _search.Text = string.Empty;
                _host.Window?.EndInputFocus(restoreForeground: true);
                e.Handled = true;
            }
        };
        _search.GotFocus += (_, _) =>
        {
            _search.BorderBrush = Solid(_host.Theme.Palette.Accent);
            _host.RequestInputFocus(_card, _search);
        };
        _search.LostFocus += (_, _) => _search.BorderBrush = Solid(_host.Theme.Palette.CardBorder);

        var searchIcon = new TextBlock
        {
            Text = "\uE721",
            FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Solid(host.Theme.Palette.TextMuted),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0),
            IsHitTestVisible = false,
        };
        var searchGrid = new Grid();
        searchGrid.Children.Add(_search);
        searchGrid.Children.Add(searchIcon);

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            SelectionMode = SelectionMode.Multiple,
            ItemsSource = _card.VisibleItems,
            ItemTemplate = new FuncDataTemplate<ItemViewModel>((item, _) => BuildItem(item)),
        };
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(Layoutable.MinHeightProperty, 0d),
            },
        });
        _list.SelectionChanged += (_, _) =>
        {
            var selected = _list.SelectedItems?.OfType<ItemViewModel>().ToHashSet() ?? new HashSet<ItemViewModel>();
            foreach (var item in _card.Items)
            {
                item.IsSelected = selected.Contains(item);
            }
        };

        var itemScroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 2, 2, 0),
        };

        _emptyHint = new TextBlock
        {
            Text = "拖入文件开始收纳\n右键可新建文件夹",
            TextAlignment = TextAlignment.Center,
            FontSize = 12.5,
            LineHeight = 20,
            Foreground = Solid(host.Theme.Palette.TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        _noResultHint = new TextBlock
        {
            Text = "没有匹配的收纳项",
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            Foreground = Solid(host.Theme.Palette.TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false,
        };

        var emptyLayer = new Grid { IsHitTestVisible = false };
        emptyLayer.Children.Add(_emptyHint);
        emptyLayer.Children.Add(_noResultHint);

        _dropOverlay = new Border
        {
            Background = Solid(host.Theme.Palette.DropOverlay),
            BorderBrush = Solid(host.Theme.Palette.Accent),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(4),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "松开以收纳到此收纳盒",
                FontFamily = new FontFamily("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI"),
                Foreground = Solid(host.Theme.Palette.TextPrimary),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        _root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6,
        };
        _root.Children.Add(searchGrid);
        var body = new Grid();
        body.Children.Add(itemScroll);
        body.Children.Add(emptyLayer);
        Grid.SetRow(body, 1);
        _root.Children.Add(body);
        _root.Children.Add(_dropOverlay);
        Grid.SetRowSpan(_dropOverlay, 2);

        Content = _root;
        ContextMenu = BuildBlankMenu();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

        _card.VisibleItems.CollectionChanged += (_, _) => UpdateEmptyState();
        _card.PropertyChanged += OnCardPropertyChanged;
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();

        Subscribe();
        ApplyLayoutMode(forceRebuild: true);
        UpdateEmptyState();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        BoxService.BoxContentChanged += OnBoxContentChanged;
        AppSettingsService.Changed += OnGlobalSettingsChanged;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        BoxService.BoxContentChanged -= OnBoxContentChanged;
        AppSettingsService.Changed -= OnGlobalSettingsChanged;
    }

    private void OnGlobalSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyChrome();
            ApplyLayoutMode(forceRebuild: true);
        });
    }

    private void OnBoxContentChanged(string boxId)
    {
        if (boxId != _box.Id)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _card.RefreshItemsOnly();
            UpdateEmptyState();
        });
    }

    private void OnCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CardViewModel.IsGrid) or nameof(CardViewModel.IconSize) or nameof(CardViewModel.NameLines))
        {
            ApplyLayoutMode(forceRebuild: true);
        }
        else if (e.PropertyName is nameof(CardViewModel.SearchText))
        {
            UpdateEmptyState();
        }
    }

    private void ApplyChrome()
    {
        var palette = _host.Theme.Palette;
        _search.Background = Solid(palette.InputBackground);
        _search.Foreground = Solid(palette.TextPrimary);
        _search.BorderBrush = Solid(palette.CardBorder);
        _dropOverlay.Background = Solid(palette.DropOverlay);
        _dropOverlay.BorderBrush = Solid(palette.Accent);
        _emptyHint.Foreground = Solid(palette.TextMuted);
        _noResultHint.Foreground = Solid(palette.TextMuted);
    }

    private void ApplyLayoutMode(bool forceRebuild)
    {
        if (_card.IsGrid)
        {
            _list.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }
        else
        {
            _list.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Spacing = 2 });
        }

        if (forceRebuild)
        {
            // ItemsPanel / 尺寸变化后强制重新生成容器。
            var source = _list.ItemsSource;
            _list.ItemsSource = null;
            _list.ItemsSource = source;
        }
    }

    private Control BuildItem(ItemViewModel item)
    {
        var palette = _host.Theme.Palette;
        Border root;
        if (_card.IsGrid)
        {
            var icon = new Image
            {
                Width = _card.IconSize,
                Height = _card.IconSize,
                Stretch = Stretch.Uniform,
                Source = item.Icon,
            };
            var placeholder = new TextBlock
            {
                Text = item.KindGlyph,
                FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                FontSize = Math.Max(18, _card.IconSize * 0.5),
                Foreground = Solid(palette.TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var iconLayer = new Grid { Width = _card.IconSize, Height = _card.IconSize };
            iconLayer.Children.Add(placeholder);
            iconLayer.Children.Add(icon);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ItemViewModel.Icon))
                {
                    icon.Source = item.Icon;
                }
            };

            var name = new TextBlock
            {
                Text = item.Name,
                FontFamily = new FontFamily("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI"),
                FontSize = 11.5,
                Foreground = Solid(palette.TextPrimary),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = Math.Max(1, _card.NameLines),
                MaxWidth = Math.Max(70, _card.IconSize + 30),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var stack = new StackPanel
            {
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { iconLayer, name },
            };
            var tile = new Border
            {
                Width = Math.Max(84, _card.IconSize + 40),
                Height = _card.IconSize + Math.Max(1, _card.NameLines) * 16 + 28,
                Padding = new Thickness(6, 6, 6, 5),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(9),
                Background = Brushes.Transparent,
                Child = stack,
            };
            root = tile;
            void RefreshTile()
            {
                tile.Background = item.IsSelected
                    ? Solid(Color.FromArgb(34, palette.Accent.R, palette.Accent.G, palette.Accent.B))
                    : Brushes.Transparent;
            }
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ItemViewModel.IsSelected))
                {
                    RefreshTile();
                }
            };
            RefreshTile();
        }
        else
        {
            var icon = new Image
            {
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                Source = item.Icon,
            };
            var placeholder = new TextBlock
            {
                Text = item.KindGlyph,
                FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                FontSize = 17,
                Foreground = Solid(palette.TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var iconLayer = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
            iconLayer.Children.Add(placeholder);
            iconLayer.Children.Add(icon);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ItemViewModel.Icon))
                {
                    icon.Source = item.Icon;
                }
            };

            var text = new StackPanel
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = item.Name,
                        FontFamily = new FontFamily("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI"),
                        FontSize = 12.5,
                        Foreground = Solid(palette.TextPrimary),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    new TextBlock
                    {
                        Text = item.TypeHint,
                        FontFamily = new FontFamily("Segoe UI Variable Text,Segoe UI,Microsoft YaHei UI"),
                        FontSize = 10.5,
                        Foreground = Solid(palette.TextMuted),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), ColumnSpacing = 8 };
            grid.Children.Add(iconLayer);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var row = new Border
            {
                Height = 48,
                Padding = new Thickness(8, 4),
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Background = Brushes.Transparent,
                Child = grid,
            };
            root = row;
            void RefreshRow()
            {
                row.Background = item.IsSelected
                    ? Solid(Color.FromArgb(34, palette.Accent.R, palette.Accent.G, palette.Accent.B))
                    : Brushes.Transparent;
            }
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ItemViewModel.IsSelected))
                {
                    RefreshRow();
                }
            };
            RefreshRow();
        }

        if (item.IsMissing)
        {
            root.Opacity = 0.55;
        }

        ToolTip.SetTip(root, item.Tooltip);
        var menu = BuildItemMenu(item);
        root.ContextMenu = menu;
        root.DoubleTapped += (_, e) =>
        {
            if (AppSettingsService.Current.DoubleClickToOpen)
            {
                OpenItem(item);
                e.Handled = true;
            }
        };
        root.AddHandler(PointerPressedEvent, (_, e) => OnItemPointerPressed(item, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(PointerMovedEvent, (_, e) => OnItemPointerMoved(item, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(PointerReleasedEvent, (_, e) => OnItemPointerReleased(item, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        root.PointerEntered += (_, _) =>
        {
            if (!item.IsSelected)
            {
                root.Background = Solid(palette.Hover);
            }
        };
        root.PointerExited += (_, _) =>
        {
            root.Background = item.IsSelected
                ? Solid(Color.FromArgb(34, palette.Accent.R, palette.Accent.G, palette.Accent.B))
                : Brushes.Transparent;
        };
        return root;
    }

    private Point _dragStart;
    private ItemViewModel? _pressedItem;
    private bool _pressed;
    private bool _dragStarted;

    private void OnItemPointerPressed(ItemViewModel item, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = true;
        _pressedItem = item;
        _dragStart = e.GetPosition(this);
        _dragStarted = false;
        _dragItems = GetSelectedItems(item);
    }

    private IReadOnlyList<ItemViewModel> _dragItems = Array.Empty<ItemViewModel>();

    private IReadOnlyList<ItemViewModel> GetSelectedItems(ItemViewModel fallback)
    {
        var selected = _list.SelectedItems?.OfType<ItemViewModel>().ToList() ?? new List<ItemViewModel>();
        if (selected.Count == 0 || !selected.Contains(fallback))
        {
            return new[] { fallback };
        }

        return selected;
    }

    private void OnItemPointerMoved(ItemViewModel item, PointerEventArgs e)
    {
        if (!_pressed || _dragStarted || _pressedItem is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStart.X) < 6 && Math.Abs(point.Y - _dragStart.Y) < 6)
        {
            return;
        }

        _dragStarted = true;
        _pressed = false;
        _host.BeginItemDrag(_card, _dragItems, e);
    }

    private void OnItemPointerReleased(ItemViewModel item, PointerReleasedEventArgs e)
    {
        _pressed = false;
        _pressedItem = null;
        _dragStarted = false;
    }

    private void OpenItem(ItemViewModel item)
    {
        if (_card.Box is null)
        {
            return;
        }

        BoxService.OpenItem(_card.Box, item.Model);
    }

    private void UpdateEmptyState()
    {
        var search = _card.SearchText.Trim();
        _emptyHint.IsVisible = _card.Items.Count == 0;
        _noResultHint.IsVisible = _card.Items.Count > 0 && _card.VisibleItems.Count == 0 && search.Length > 0;
        _emptyHint.Text = _box.Type == BoxType.Normal
            ? "把文件拖到这里\n或右键新建文件夹"
            : _box.Type == BoxType.Mapped
                ? "把文件拖到这里\n只保存引用，不改动源文件"
                : $"目标文件夹\n{_box.TargetPath}";
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var paths = GetDropPaths(e);
        e.DragEffects = paths.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        _dropOverlay.IsVisible = paths.Count > 0;
        _dropOverlay.Child = new TextBlock
        {
            Text = $"松开以收纳到「{_box.Name}」",
            Foreground = Solid(_host.Theme.Palette.TextPrimary),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        _dropOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _dropOverlay.IsVisible = false;
        var paths = GetDropPaths(e);
        if (paths.Count == 0)
        {
            return;
        }

        e.Handled = true;
        await ImportPathsAsync(paths);
    }

    private static List<string> GetDropPaths(DragEventArgs e)
    {
        var result = new List<string>();
        try
        {
            var files = e.Data.GetFiles();
            if (files is not null)
            {
                foreach (var file in files)
                {
                    var path = file.TryGetLocalPath();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        result.Add(path);
                    }
                }
            }
            else
            {
                var names = e.Data.GetFileNames();
                if (names is not null)
                {
                    result.AddRange(names.Where(PathUtil.Exists));
                }
            }
        }
        catch
        {
            // 其他应用提供的非文件数据。
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        var owner = _host.Window;
        if (owner is null || _card.Box is null)
        {
            return;
        }

        try
        {
            var results = await ImportService.ImportPathsAsync(
                _card.Box,
                paths,
                new UiImportInteraction(owner, _host));
            var success = results.Count(r => r.Success);
            var skipped = results.Count(r => !r.Success);
            if (success > 0)
            {
                _host.ShowToast($"已收纳 {success} 项到「{_box.Name}」" + (skipped > 0 ? $"，{skipped} 项被跳过" : string.Empty));
                _card.RefreshItemsOnly();
            }
            else if (skipped > 0)
            {
                var first = results.FirstOrDefault(r => !r.Success);
                _host.ShowToast(first?.Error ?? "没有项目被收纳");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("拖入收纳失败", ex);
            _host.ShowToast($"收纳失败：{ex.Message}");
        }
    }

    private ContextMenu BuildBlankMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("新建收纳盒", () => _host.ViewModel.AddBox(null, BoxType.Normal, null)));
        menu.Items.Add(Menu("新建文件夹", CreateFolder));
        menu.Items.Add(Menu("粘贴路径", PastePaths));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("打开所在位置", () =>
        {
            if (_box.Type == BoxType.Target && !string.IsNullOrEmpty(_box.TargetPath))
            {
                ShellService.Open(_box.TargetPath);
            }
            else
            {
                Directory.CreateDirectory(AppPaths.BoxStorage(_box.Id));
                ShellService.Open(AppPaths.BoxStorage(_box.Id));
            }
        }));
        menu.Items.Add(Menu("搜索", () => _search.Focus()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("打开组件设置", () => _host.OpenSettings()));
        return menu;
    }

    private ContextMenu BuildItemMenu(ItemViewModel item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("打开", () => OpenItem(item)));
        menu.Items.Add(Menu("在资源管理器中显示", () =>
        {
            if (_card.Box is not null)
            {
                BoxService.RevealItem(_card.Box, item.Model);
            }
        }));
        menu.Items.Add(Menu("重命名", async () =>
        {
            if (_card.Box is null || _host.Window is null)
            {
                return;
            }

            var name = await DialogService.PromptAsync(_host.Window, "重命名", "输入新的名称", item.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                BoxService.RenameItem(_card.Box, item.Model, name);
                _card.RefreshItemsOnly();
            }
            catch (Exception ex)
            {
                _host.ShowToast($"重命名失败：{ex.Message}");
            }
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("创建桌面快捷方式", () =>
        {
            var created = ShellService.CreateShortcutOnDesktop(item.Name, item.RealPath, item.Model.ShortcutTarget);
            _host.ShowToast(created is null ? "无法创建快捷方式" : "已在桌面创建快捷方式");
        }));
        menu.Items.Add(Menu("复制路径", async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(item.RealPath);
                _host.ShowToast("路径已复制");
            }
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("属性", () => BoxService.ShowItemProperties(_card.Box!, item.Model, _host.WindowHandle)));
        menu.Items.Add(Menu("删除", async () =>
        {
            if (_card.Box is null || _host.Window is null)
            {
                return;
            }

            if (AppSettingsService.Current.ConfirmDelete &&
                !await DialogService.ConfirmAsync(_host.Window, "删除收纳项", $"确定删除「{item.Name}」？普通收纳盒中的文件会还原到原位置。"))
            {
                return;
            }

            BoxService.DeleteItems(_card.Box, new[] { item.Model });
            _card.RefreshItemsOnly();
        }));
        return menu;
    }

    private static MenuItem Menu(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Menu(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                LogService.Error("菜单操作失败", ex);
            }
        };
        return item;
    }

    private void CreateFolder()
    {
        try
        {
            var storage = AppPaths.BoxStorage(_box.Id);
            Directory.CreateDirectory(storage);
            var path = PathUtil.UniquePathIn(storage, "新建文件夹");
            Directory.CreateDirectory(path);
            ItemRepository.Insert(new BoxItem
            {
                BoxId = _box.Id,
                Kind = ItemKind.Folder,
                DisplayName = Path.GetFileName(path),
                SourcePath = path,
                StoredPath = path,
                SortIndex = ItemRepository.MaxSortIndex(_box.Id) + 1,
            });
            _card.RefreshItemsOnly();
            _host.ShowToast("已新建文件夹");
        }
        catch (Exception ex)
        {
            _host.ShowToast($"新建文件夹失败：{ex.Message}");
        }
    }

    private async void PastePaths()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            var text = await clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                _host.ShowToast("剪贴板中没有路径或网址");
                return;
            }

            var paths = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().Trim('"'))
                .Where(PathUtil.Exists)
                .ToList();

            if (paths.Count > 0)
            {
                await ImportPathsAsync(paths);
                return;
            }

            if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                _card.Box is not null)
            {
                var results = await ImportService.ImportUrlAsync(_card.Box, text.Trim(), new UiImportInteraction(_host.Window!, _host));
                if (results.Any(r => r.Success))
                {
                    _card.RefreshItemsOnly();
                    _host.ShowToast("网址已收纳");
                }
                else
                {
                    _host.ShowToast(results.FirstOrDefault()?.Error ?? "网址收纳失败");
                }

                return;
            }

            _host.ShowToast("剪贴板中没有可用路径");
        }
        catch (Exception ex)
        {
            _host.ShowToast($"粘贴失败：{ex.Message}");
        }
    }

    private static SolidColorBrush Solid(Color color) => new(color);
}
