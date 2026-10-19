using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using PocketDesk.Shared.Widgets;
using Windows.UI;

namespace PocketDesk.Settings.Views;

/// <summary>
/// 组件管理：类型发现、实例启停/排序，以及完全由 IWidget.GetSettingsSchema() 驱动的动态配置表单。
/// 页面里没有任何天气专用控件；新增组件只要带上 Schema 就能在这里配置。
/// </summary>
public sealed class WidgetsPage : UserControl
{
    private readonly MainWindow _owner;
    private readonly SettingsSnapshot _snapshot;
    private readonly StackPanel _detailHost = new() { Spacing = 12 };
    private readonly DispatcherQueueTimer _saveTimer;
    private WidgetInstance? _selected;
    private Dictionary<string, string> _editSettings = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading = true;

    public WidgetsPage(MainWindow owner, SettingsSnapshot snapshot)
    {
        _owner = owner;
        _snapshot = snapshot;
        _selected = snapshot.Widgets.FirstOrDefault(w => w.WidgetTypeId != AppInfo.BoxWidgetId)
                    ?? snapshot.Widgets.FirstOrDefault();

        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(450);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += OnSaveTimerTick;
        Unloaded += (_, _) => _saveTimer.Stop();

        Content = Build();
        _loading = false;
        ShowDetail();
    }

    private FrameworkElement Build()
    {
        var page = new StackPanel { Spacing = 0 };
        page.Children.Add(Ui.PageTitle("组件"));
        page.Children.Add(Ui.PageSubtitle(
            "PocketDesk-New 是组件宿主。收纳盒是第一个内置组件；下方“可用组件类型”来自宿主实际加载的 IWidget 实现。"));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(340) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            ColumnSpacing = 20,
        };

        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(Ui.SectionTitle("可用组件类型"));
        foreach (var type in _snapshot.WidgetTypes)
        {
            left.Children.Add(TypeCard(type));
        }

        left.Children.Add(Ui.SectionTitle("已安装组件"));
        if (_snapshot.Widgets.Count == 0)
        {
            left.Children.Add(Ui.Caption("还没有任何组件实例。"));
        }

        foreach (var instance in _snapshot.Widgets)
        {
            left.Children.Add(InstanceCard(instance));
        }

        grid.Children.Add(left);
        Grid.SetColumn(_detailHost, 1);
        grid.Children.Add(_detailHost);
        page.Children.Add(grid);
        return page;
    }

    private Border TypeCard(WidgetTypeInfo type)
    {
        var top = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 10,
        };
        var icon = Ui.IconTile(type.Glyph, 36, 18);
        top.Children.Add(icon);

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = type.DisplayName, FontSize = 14, Foreground = Ui.TextPrimary });
        text.Children.Add(new TextBlock
        {
            Text = type.IsBuiltIn ? "内置组件" : "插件组件",
            FontSize = 11,
            Foreground = Ui.TextSecondary,
        });
        Grid.SetColumn(text, 1);
        top.Children.Add(text);

        var add = Ui.Button("添加", async (_, _) =>
        {
            try
            {
                await _owner.Client.SendAsync<WidgetInstance>(IpcCommands.AddWidget, new AddWidgetPayload
                {
                    TypeId = type.TypeId,
                });
                await _owner.RefreshAsync();
                _owner.ShowSuccess($"已添加「{type.DisplayName}」");
            }
            catch (Exception ex)
            {
                _owner.ShowError(ex.Message);
            }
        }, "\uE710");
        add.IsEnabled = !type.IsBuiltIn && type.SupportsMultipleInstances;
        add.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(add, 2);
        top.Children.Add(add);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(top);
        panel.Children.Add(new TextBlock
        {
            Text = type.Description,
            FontSize = 12,
            Foreground = Ui.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{type.Schema.Fields.Count} 个 Schema 配置项",
            FontSize = 11,
            Foreground = Ui.TextSecondary,
        });

        return new Border
        {
            Background = Ui.CardBackground,
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = panel,
        };
    }

    private Border InstanceCard(WidgetInstance instance)
    {
        var type = _snapshot.WidgetTypes.FirstOrDefault(t => t.TypeId == instance.WidgetTypeId);
        var selected = _selected?.Id == instance.Id;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 10,
        };
        var icon = Ui.IconTile(
            type?.Glyph ?? "\uE8B7",
            32,
            16,
            selected ? Ui.Accent : Ui.TextSecondary);
        grid.Children.Add(icon);

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = instance.DisplayName,
            FontSize = 13,
            Foreground = Ui.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = type?.DisplayName ?? instance.WidgetTypeId,
            FontSize = 11,
            Foreground = Ui.TextSecondary,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var visibility = new ToggleSwitch
        {
            IsOn = instance.IsVisible,
            IsHitTestVisible = true,
            MinWidth = 0,
            OnContent = null,
            OffContent = null,
        };
        visibility.Toggled += async (_, _) =>
        {
            try
            {
                await _owner.Client.SendCommandAsync(IpcCommands.SetWidgetVisible, new VisibilityPayload
                {
                    Id = instance.Id,
                    Visible = visibility.IsOn,
                });
                await _owner.RefreshAsync();
            }
            catch (Exception ex)
            {
                _owner.ShowError(ex.Message);
            }
        };
        Grid.SetColumn(visibility, 2);
        grid.Children.Add(visibility);

        var button = new Button
        {
            Content = grid,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = selected
                ? new SolidColorBrush(Color.FromArgb(24, 76, 194, 255))
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(selected ? 1 : 0),
            BorderBrush = selected ? Ui.Accent : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
        };
        button.Click += (_, _) =>
        {
            _loading = true;
            _selected = instance;
            _editSettings = new Dictionary<string, string>(instance.Settings, StringComparer.OrdinalIgnoreCase);
            Content = Build();
            _loading = false;
            ShowDetail();
        };

        return new Border
        {
            Background = Ui.CardBackground,
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = button,
        };
    }

    private void ShowDetail()
    {
        _detailHost.Children.Clear();
        if (_selected is null)
        {
            _detailHost.Children.Add(Ui.Card(new TextBlock
            {
                Text = "选择左侧组件查看其 Schema 配置。",
                Foreground = Ui.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
            }));
            return;
        }

        var type = _snapshot.WidgetTypes.FirstOrDefault(t => t.TypeId == _selected.WidgetTypeId);
        _editSettings = new Dictionary<string, string>(_selected.Settings, StringComparer.OrdinalIgnoreCase);

        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(new TextBlock
        {
            Text = _selected.DisplayName,
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Ui.TextPrimary,
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{type?.DisplayName ?? "组件"} · {(type?.IsBuiltIn == true ? "内置" : "插件")} · {_selected.WidgetTypeId}",
            FontSize = 12,
            Foreground = Ui.TextSecondary,
        });
        _detailHost.Children.Add(header);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (type is { IsBuiltIn: false })
        {
            actions.Children.Add(Ui.Button("移除组件", async (_, _) => await RemoveSelectedAsync(), "\uE74D"));
        }

        actions.Children.Add(Ui.Button("上移一层", async (_, _) => await MoveSelectedAsync(-1), "\uE74A"));
        actions.Children.Add(Ui.Button("下移一层", async (_, _) => await MoveSelectedAsync(1), "\uE74B"));
        _detailHost.Children.Add(actions);

        var schema = type?.Schema ?? new WidgetSettingsSchema();
        if (schema.Fields.Count == 0)
        {
            _detailHost.Children.Add(Ui.Card(Ui.Caption("该组件没有声明可配置项。")));
            return;
        }

        var form = new StackPanel { Spacing = 14 };
        form.Children.Add(new TextBlock
        {
            Text = schema.Title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Ui.TextPrimary,
        });
        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            form.Children.Add(Ui.Caption(schema.Description));
        }

        foreach (var field in schema.Fields)
        {
            form.Children.Add(BuildField(field));
        }

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(Ui.Button("恢复默认值", (_, _) =>
        {
            _editSettings.Clear();
            foreach (var field in schema.Fields)
            {
                if (!string.IsNullOrEmpty(field.DefaultValue))
                {
                    _editSettings[field.Key] = field.DefaultValue!;
                }
            }

            QueueSave();
            ShowDetail();
        }, "\uE777"));
        form.Children.Add(footer);
        form.Children.Add(new TextBlock
        {
            Text = "此表单由组件声明的 WidgetSettingsSchema 动态生成；设置界面不包含任何组件专用代码。",
            FontSize = 11,
            Foreground = Ui.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });

        _detailHost.Children.Add(Ui.Card(form, 18));
    }

    private FrameworkElement BuildField(WidgetSettingField field)
    {
        var value = _editSettings.TryGetValue(field.Key, out var current)
            ? current
            : field.DefaultValue ?? string.Empty;

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Ui.TextPrimary,
        });
        if (!string.IsNullOrWhiteSpace(field.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = field.Description,
                FontSize = 12,
                Foreground = Ui.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        switch (field.Type)
        {
            case WidgetSettingTypes.Toggle:
            {
                var toggle = Ui.Toggle(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
                toggle.Toggled += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    _editSettings[field.Key] = toggle.IsOn ? "true" : "false";
                    QueueSave();
                };
                panel.Children.Add(toggle);
                break;
            }
            case WidgetSettingTypes.Number:
            {
                var number = new NumberBox
                {
                    Value = double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0,
                    Minimum = field.Min ?? double.MinValue,
                    Maximum = field.Max ?? double.MaxValue,
                    SmallChange = field.Step ?? 1,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    MinWidth = 220,
                };
                number.ValueChanged += (_, e) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    _editSettings[field.Key] = e.NewValue.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    QueueSave();
                };
                panel.Children.Add(number);
                break;
            }
            case WidgetSettingTypes.Choice:
            {
                var combo = Ui.Combo((field.Choices ?? new List<WidgetSettingChoice>())
                    .Select(c => (c.Value, c.Label)).ToArray());
                Ui.SelectCombo(combo, value);
                combo.SelectionChanged += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    _editSettings[field.Key] = Ui.ComboValue(combo) ?? string.Empty;
                    QueueSave();
                };
                panel.Children.Add(combo);
                break;
            }
            case WidgetSettingTypes.Color:
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                var input = Ui.TextInput(value, field.Placeholder ?? "#4CC2FF");
                input.Width = 160;
                var preview = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Ui.ParseColor(value, Color.FromArgb(255, 76, 194, 255))),
                    BorderBrush = Ui.CardStroke,
                    BorderThickness = new Thickness(1),
                };
                input.TextChanged += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    preview.Background = new SolidColorBrush(Ui.ParseColor(input.Text,
                        Color.FromArgb(255, 76, 194, 255)));
                    _editSettings[field.Key] = input.Text.Trim();
                    QueueSave();
                };
                row.Children.Add(input);
                row.Children.Add(preview);
                panel.Children.Add(row);
                break;
            }
            default:
            {
                var input = Ui.TextInput(value, field.Placeholder);
                input.TextChanged += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    _editSettings[field.Key] = input.Text;
                    QueueSave();
                };
                panel.Children.Add(input);
                break;
            }
        }

        return new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(6),
            Background = Ui.IsDark
                ? new SolidColorBrush(Color.FromArgb(24, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)),
            Child = panel,
        };
    }

    private void QueueSave()
    {
        if (_selected is null)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_selected is null)
        {
            return;
        }

        try
        {
            await _owner.SaveWidgetSettingsAsync(_selected.Id, _editSettings);
            _owner.ShowMessage(InfoBarSeverity.Success, "已应用", $"「{_selected.DisplayName}」的配置已同步到桌面组件层。");
        }
        catch (Exception ex)
        {
            _owner.ShowError(ex.Message);
        }
    }

    private async Task RemoveSelectedAsync()
    {
        if (_selected is null || _owner.Snapshot is null)
        {
            return;
        }

        var type = _snapshot.WidgetTypes.FirstOrDefault(t => t.TypeId == _selected.WidgetTypeId);
        if (type?.IsBuiltIn == true)
        {
            _owner.ShowMessage(InfoBarSeverity.Informational, "内置组件", "收纳盒请到“收纳盒”页面管理。");
            return;
        }

        var dialog = _owner.CreateDialog(
            "移除组件",
            "移除",
            "取消",
            new TextBlock
            {
                Text = $"确定移除「{_selected.DisplayName}」？组件实例的设置会被删除，但不会影响其他组件。",
                TextWrapping = TextWrapping.Wrap,
            });
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(IpcCommands.RemoveWidget, new IdPayload { Id = _selected.Id });
        }, "组件已移除");
    }

    private async Task MoveSelectedAsync(int delta)
    {
        if (_selected is null || _owner.Snapshot is null)
        {
            return;
        }

        var ids = _snapshot.Widgets.Select(w => w.Id).ToList();
        var index = ids.IndexOf(_selected.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return;
        }

        (ids[index], ids[target]) = (ids[target], ids[index]);
        await _owner.RunAsync(async () =>
        {
            await _owner.Client.SendCommandAsync(IpcCommands.ReorderWidgets, new ReorderPayload { OrderedIds = ids });
        });
    }
}
