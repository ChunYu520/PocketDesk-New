using System.Text.Json;
using PocketDesk.Core;
using PocketDesk.Core.Tests.Infrastructure;
using PocketDesk.Shared;
using PocketDesk.Shared.Ipc;
using PocketDesk.Shared.Widgets;
using PocketDesk.Widgets.Weather;

namespace PocketDesk.Core.Tests;

public sealed class WidgetTests
{
    [Fact]
    public void WeatherWidget_DeclaresSchemaWithoutHostKnowledge()
    {
        var widget = new WeatherWidget();
        var schema = widget.GetSettingsSchema();

        Assert.Equal(AppInfo.WeatherWidgetId, widget.Id);
        Assert.False(widget.IsBuiltIn);
        Assert.Contains(schema.Fields, f => f.Key == "city" && f.Type == WidgetSettingTypes.Text);
        Assert.Contains(schema.Fields, f => f.Key == "refreshMinutes" && f.Type == WidgetSettingTypes.Number);
        Assert.Contains(schema.Fields, f => f.Key == "units" && f.Type == WidgetSettingTypes.Choice);
        Assert.Contains(schema.Fields, f => f.Key == "showFeelsLike" && f.Type == WidgetSettingTypes.Toggle);
        Assert.Contains(schema.Fields, f => f.Key == "accent" && f.Type == WidgetSettingTypes.Color);
    }

    [Fact]
    public void Schema_SerializesRoundTrip_ForIpcAndSettingsWindow()
    {
        var schema = new WeatherWidget().GetSettingsSchema();

        var json = JsonSerializer.Serialize(schema, JsonDefaults.Compact);
        var restored = JsonSerializer.Deserialize<WidgetSettingsSchema>(json, JsonDefaults.Compact);

        Assert.NotNull(restored);
        Assert.Equal(schema.WidgetTypeId, restored!.WidgetTypeId);
        Assert.Equal(schema.Fields.Count, restored.Fields.Count);
        Assert.Equal("城市名称", restored.Fields.Single(f => f.Key == "city").Label);
    }

    [Fact]
    public void WidgetRepository_RoundTripsSettingsAndPlacement()
    {
        using var workspace = new TempWorkspace();
        var instance = new WidgetInstance
        {
            WidgetTypeId = AppInfo.WeatherWidgetId,
            DisplayName = "天气",
            X = 120,
            Y = 80,
            Width = 320,
            Height = 220,
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["city"] = "上海",
                ["units"] = "c",
            },
        };

        WidgetRepository.Insert(instance);

        var loaded = WidgetRepository.Get(instance.Id);
        Assert.NotNull(loaded);
        Assert.Equal("上海", loaded!.GetSetting("city"));
        Assert.Equal(320, loaded.Width);

        WidgetRepository.UpdateSettings(instance.Id, new Dictionary<string, string>
        {
            ["city"] = "北京",
            ["refreshMinutes"] = "15",
        });
        var updated = WidgetRepository.Get(instance.Id)!;
        Assert.Equal("北京", updated.GetSetting("city"));
        Assert.Equal(15, updated.GetSetting("refreshMinutes", 0));
    }

    [Fact]
    public void WidgetRepository_ReorderAndDelete()
    {
        using var workspace = new TempWorkspace();
        var first = new WidgetInstance { WidgetTypeId = "test", DisplayName = "A", ZOrder = 0 };
        var second = new WidgetInstance { WidgetTypeId = "test", DisplayName = "B", ZOrder = 1 };
        WidgetRepository.Insert(first);
        WidgetRepository.Insert(second);

        WidgetRepository.Reorder(new[] { second.Id, first.Id });

        Assert.Equal(0, WidgetRepository.Get(second.Id)!.ZOrder);
        Assert.Equal(1, WidgetRepository.Get(first.Id)!.ZOrder);

        WidgetRepository.Delete(second.Id);
        Assert.Null(WidgetRepository.Get(second.Id));
    }

    [Fact]
    public void SettingsSnapshot_SerializesAcrossProcessBoundary()
    {
        var snapshot = new SettingsSnapshot
        {
            Settings = new AppSettings { DefaultCornerRadius = 22, Theme = "Dark" },
            WidgetTypes =
            {
                new WidgetTypeInfo
                {
                    TypeId = AppInfo.WeatherWidgetId,
                    DisplayName = "天气",
                    Schema = new WeatherWidget().GetSettingsSchema(),
                },
            },
            Widgets =
            {
                new WidgetInstance
                {
                    WidgetTypeId = AppInfo.WeatherWidgetId,
                    DisplayName = "天气",
                    Settings = new Dictionary<string, string> { ["city"] = "上海" },
                },
            },
        };

        var json = JsonSerializer.Serialize(snapshot, JsonDefaults.Compact);
        var restored = JsonSerializer.Deserialize<SettingsSnapshot>(json, JsonDefaults.Compact);

        Assert.NotNull(restored);
        Assert.Equal(22, restored!.Settings.DefaultCornerRadius);
        Assert.Single(restored.WidgetTypes);
        Assert.Equal("上海", restored.Widgets[0].GetSetting("city"));
    }

    [Fact]
    public void AppSettings_NormalizeClampsValues()
    {
        var settings = new AppSettings
        {
            DefaultCornerRadius = 999,
            CardOpacity = 0,
            CardBorderOpacity = 5,
            ShadowStrength = -20,
            DefaultIconSize = 999,
        };

        settings.Normalize();

        Assert.Equal(32, settings.DefaultCornerRadius);
        Assert.Equal(0.2, settings.CardOpacity, 3);
        Assert.Equal(1, settings.CardBorderOpacity, 3);
        Assert.Equal(0, settings.ShadowStrength);
        Assert.Equal(128, settings.DefaultIconSize);
    }
}
