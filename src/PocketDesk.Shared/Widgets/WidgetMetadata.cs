namespace PocketDesk.Shared.Widgets;

/// <summary>Widget 设置项类型，用于设置界面动态渲染。</summary>
public static class WidgetSettingTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Toggle = "toggle";
    public const string Choice = "choice";
    public const string Color = "color";
}

/// <summary>下拉选项。</summary>
public sealed class WidgetSettingChoice
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public WidgetSettingChoice()
    {
    }

    public WidgetSettingChoice(string value, string label)
    {
        Value = value;
        Label = label;
    }
}

/// <summary>单个设置项描述。设置界面不硬编码任何组件字段。</summary>
public sealed class WidgetSettingField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = WidgetSettingTypes.Text;
    public string? DefaultValue { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public string? Placeholder { get; set; }
    public List<WidgetSettingChoice>? Choices { get; set; }
}

/// <summary>组件设置 Schema，由组件自身声明，设置界面负责渲染。</summary>
public sealed class WidgetSettingsSchema
{
    public string WidgetTypeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<WidgetSettingField> Fields { get; set; } = new();
}

/// <summary>组件类型元信息，Host 通过 IPC 提供给设置界面。</summary>
public sealed class WidgetTypeInfo
{
    public string TypeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Glyph { get; set; } = "\uE8B7";
    public bool IsBuiltIn { get; set; }
    public bool SupportsMultipleInstances { get; set; }
    public WidgetSettingsSchema Schema { get; set; } = new();
}

/// <summary>
/// 组件实例。收纳盒实例与 boxes 表共用 Id，其余组件保存在 widgets 表中。
/// 位置与尺寸使用宿主窗口内的 DIP 坐标。
/// </summary>
public sealed class WidgetInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WidgetTypeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int ZOrder { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 340;
    public double Height { get; set; } = 300;
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? GetSetting(string key) =>
        Settings.TryGetValue(key, out var value) ? value : null;

    public string GetSetting(string key, string fallback) =>
        Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

    public double GetSetting(string key, double fallback) =>
        Settings.TryGetValue(key, out var value) &&
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public bool GetSetting(string key, bool fallback) =>
        Settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
}
