using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PocketDesk.Core;
using PocketDesk.Host.Services;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets;

namespace PocketDesk.Host.ViewModels;

/// <summary>组件层的主视图模型：卡片集合、全局设置与设置界面 IPC 所需的状态快照。</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly HostServices _host;
    private readonly DateTime _startedAt = DateTime.Now;
    private string? _recoveryMessage;

    public MainViewModel(HostServices host)
    {
        _host = host;
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CardCount));
    }

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    public int CardCount => Cards.Count;

    public RecoveryReport? Recovery { get; private set; }

    public void Load(RecoveryReport? recovery)
    {
        Recovery = recovery;
        _recoveryMessage = recovery?.HasIssues == true ? recovery.ToDisplayString() : null;

        Cards.Clear();
        var boxWidget = _host.Widgets.Get(AppInfo.BoxWidgetId);
        if (boxWidget is null)
        {
            throw new InvalidOperationException("内置收纳盒组件未注册。");
        }

        var boxes = BoxService.GetBoxes();
        foreach (var box in boxes.Where(b => b.IsVisible))
        {
            var card = CreateBoxCard(box, boxWidget);
            if (card is not null)
            {
                Cards.Add(card);
            }
        }

        foreach (var instance in WidgetRepository.GetAll())
        {
            if (!instance.IsVisible)
            {
                continue;
            }

            var widget = _host.Widgets.Get(instance.WidgetTypeId);
            if (widget is null)
            {
                LogService.Warn($"跳过未知组件实例：{instance.WidgetTypeId} ({instance.DisplayName})");
                continue;
            }

            var card = CreateWidgetCard(instance, widget);
            Cards.Add(card);
        }

        SortCards();
        foreach (var card in Cards)
        {
            card.Initialize();
            EnsureContent(card);
        }

        if (!AppSettingsService.Current.RestoreLayoutOnStartup)
        {
            // 不恢复上次摆位时，仍保留保存的尺寸，只把卡片重新按默认级联摆放。
            var index = 0;
            foreach (var card in Cards)
            {
                var placement = BoxService.DefaultPlacement(index++);
                card.X = placement.X;
                card.Y = placement.Y;
            }
        }

        LogService.Info($"组件层已加载 {Cards.Count} 张卡片（收纳盒 {Cards.Count(c => c.IsBox)}，其他组件 {Cards.Count(c => !c.IsBox)}）");
    }

    public void ReloadAll()
    {
        var previous = _recoveryMessage;
        Load(Recovery);
        _recoveryMessage = previous;
    }

    private CardViewModel? CreateBoxCard(Box box, IWidget widget)
    {
        var card = new CardViewModel(box, widget, _host);
        card.Context = new WidgetContext(card, _host);
        return card;
    }

    private CardViewModel CreateWidgetCard(WidgetInstance instance, IWidget widget)
    {
        var card = new CardViewModel(instance, widget, _host);
        card.Context = new WidgetContext(card, _host);
        return card;
    }

    private void EnsureContent(CardViewModel card)
    {
        card.Content ??= card.Widget.CreateView(card.Context!);
    }

    public CardViewModel AddBox(string? name, BoxType type, string? targetPath)
    {
        var index = Cards.Count;
        var placement = NextPlacement(index);
        var box = BoxService.CreateBox(name ?? string.Empty, type, targetPath, placement);
        var widget = _host.Widgets.Get(AppInfo.BoxWidgetId)
                     ?? throw new InvalidOperationException("内置收纳盒组件未注册。");
        var card = CreateBoxCard(box, widget)!;
        Cards.Add(card);
        card.Initialize();
        EnsureContent(card);
        SortCards();
        BringToFront(card);
        _host.Window?.SyncCards();
        return card;
    }

    public CardViewModel? AddWidget(string typeId, string? displayName = null)
    {
        var widget = _host.Widgets.Get(typeId);
        if (widget is null)
        {
            throw new InvalidOperationException($"未找到组件类型：{typeId}");
        }

        if (!widget.SupportsMultipleInstances &&
            Cards.Any(c => !c.IsBox && string.Equals(c.Instance.WidgetTypeId, typeId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("该组件不支持多个实例。");
        }

        var schema = widget.GetSettingsSchema();
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in schema.Fields)
        {
            if (!string.IsNullOrEmpty(field.DefaultValue))
            {
                settings[field.Key] = field.DefaultValue!;
            }
        }

        var index = Cards.Count;
        var placement = NextPlacement(index);
        var instance = new WidgetInstance
        {
            WidgetTypeId = typeId,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? NextWidgetName(widget, typeId)
                : displayName.Trim(),
            ZOrder = index,
            X = placement.X,
            Y = placement.Y,
            Width = placement.Width,
            Height = placement.Height,
            Settings = settings,
        };

        WidgetRepository.Insert(instance);
        var card = CreateWidgetCard(instance, widget);
        Cards.Add(card);
        card.Initialize();
        EnsureContent(card);
        BringToFront(card);
        _host.Window?.SyncCards();
        LogService.Info($"已添加组件：{widget.DisplayName} ({instance.Id})");
        return card;
    }

    private string NextWidgetName(IWidget widget, string typeId)
    {
        var existing = Cards.Count(c => !c.IsBox &&
                                        string.Equals(c.Instance.WidgetTypeId, typeId, StringComparison.OrdinalIgnoreCase));
        return existing == 0 ? widget.DisplayName : $"{widget.DisplayName} {existing + 1}";
    }

    private WindowPlacement NextPlacement(int index)
    {
        var workWidth = _host.Window?.Width ?? 1280;
        var workHeight = _host.Window?.Height ?? 720;
        const double width = 380;
        const double height = 360;
        var offset = Math.Max(0, index % 7) * 34;
        var x = Math.Clamp(140 + offset, 16, Math.Max(16, workWidth - width - 16));
        var y = Math.Clamp(120 + offset, 16, Math.Max(16, workHeight - height - 16));
        return new WindowPlacement { X = x, Y = y, Width = width, Height = height };
    }

    public void RemoveCard(CardViewModel card, bool deleteFiles = true)
    {
        if (card.IsBox && card.Box is not null)
        {
            if (deleteFiles)
            {
                BoxService.DeleteBox(card.Box);
            }
            else
            {
                BoxService.SetVisible(card.Box, false);
                BoxRepository.SetVisibility(card.Box.Id, false);
            }
        }
        else
        {
            WidgetRepository.Delete(card.Instance.Id);
        }

        Cards.Remove(card);
        _host.Window?.SyncCards();
    }

    public void RefreshCard(CardViewModel card)
    {
        card.RefreshFromModel();
        card.Content = card.Widget.CreateView(card.Context!);
        _host.Window?.RefreshCardContent(card);
    }

    public void SortCards()
    {
        var ordered = Cards.OrderBy(c => c.ZOrder).ThenBy(c => c.Instance.CreatedAt).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].ZOrder != i)
            {
                ordered[i].ZOrder = i;
            }

            Cards.Move(Cards.IndexOf(ordered[i]), i);
        }
    }

    public void BringToFront(CardViewModel card)
    {
        var max = Cards.Count == 0 ? 0 : Cards.Max(c => c.ZOrder);
        if (card.ZOrder == max)
        {
            return;
        }

        card.ZOrder = max + 1;
        if (card.IsBox && card.Box is not null)
        {
            BoxRepository.Update(card.Box);
        }
        else
        {
            WidgetRepository.Update(card.Instance);
        }

        _host.Window?.UpdateCardZOrder(card);
    }

    public void SaveAllPlacements()
    {
        foreach (var card in Cards)
        {
            try
            {
                card.SavePlacement();
            }
            catch (Exception ex)
            {
                LogService.Warn($"保存卡片位置失败：{ex.Message}");
            }
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        AppSettingsService.Replace(settings);
        _host.Theme.Apply(AppSettingsService.Current);
        foreach (var card in Cards)
        {
            card.ApplyAppearance();
        }

        _host.Window?.ApplyTheme();
    }

    public void UpdateWidgetSettings(string instanceId, Dictionary<string, string> settings)
    {
        var card = Cards.FirstOrDefault(c => string.Equals(c.Id, instanceId, StringComparison.OrdinalIgnoreCase));
        if (card is null)
        {
            throw new InvalidOperationException("组件实例不存在。");
        }

        if (card.IsBox && card.Box is not null)
        {
            var appearance = card.Box.Appearance.Clone();
            if (settings.TryGetValue("view", out var view))
            {
                appearance.View = string.Equals(view, "list", StringComparison.OrdinalIgnoreCase) ? "list" : "grid";
            }

            if (settings.TryGetValue("showItemCount", out var show))
            {
                appearance.ShowItemCount = !string.Equals(show, "false", StringComparison.OrdinalIgnoreCase);
            }

            BoxService.SaveAppearance(card.Box, appearance);
            if (settings.TryGetValue("sortField", out var sort))
            {
                BoxService.SetSort(card.Box, sort, card.Box.SortDescending);
            }

            card.RefreshFromModel();
        }
        else
        {
            card.Instance.Settings = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            WidgetRepository.UpdateSettings(card.Instance.Id, card.Instance.Settings);
            card.NotifyWidgetSettingsChanged();
            card.RefreshFromModel();
        }

        _host.Window?.RefreshCardContent(card);
    }

    public SettingsSnapshot CreateSnapshot()
    {
        var boxes = BoxService.GetBoxes();
        var counts = ItemRepository.CountAll();
        var widgetInstances = new List<WidgetInstance>();

        foreach (var box in boxes)
        {
            widgetInstances.Add(new WidgetInstance
            {
                Id = box.Id,
                WidgetTypeId = AppInfo.BoxWidgetId,
                DisplayName = box.Name,
                IsVisible = box.IsVisible,
                ZOrder = box.ZOrder,
                X = box.Placement?.X ?? 0,
                Y = box.Placement?.Y ?? 0,
                Width = box.Placement?.Width ?? 380,
                Height = box.Placement?.Height ?? 360,
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["view"] = box.Appearance.View,
                    ["showItemCount"] = box.Appearance.ShowItemCount ? "true" : "false",
                    ["sortField"] = box.SortField,
                },
                CreatedAt = box.CreatedAt,
            });
        }

        foreach (var instance in WidgetRepository.GetAll())
        {
            widgetInstances.Add(instance);
        }

        return new SettingsSnapshot
        {
            Settings = AppSettingsService.Current.Clone(),
            WidgetTypes = _host.Widgets.GetTypeInfos().ToList(),
            Widgets = widgetInstances,
            Boxes = boxes.Select(b => new BoxSummary
            {
                Id = b.Id,
                Name = b.Name,
                Type = b.Type.ToString(),
                TargetPath = b.TargetPath,
                IsVisible = b.IsVisible,
                IsPinned = b.IsPinned,
                ZOrder = b.ZOrder,
                ItemCount = counts.TryGetValue(b.Id, out var count) ? count : 0,
                View = b.Appearance.View,
                CreatedAt = b.CreatedAt,
            }).ToList(),
            HostVersion = AppInfo.Version,
            HostExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PocketDesk.Host.exe"),
            DataRoot = AppPaths.Root,
            DatabasePath = AppPaths.DatabasePath,
            HostStartedAt = _startedAt,
            LastRecoveryMessage = _recoveryMessage,
        };
    }
}
