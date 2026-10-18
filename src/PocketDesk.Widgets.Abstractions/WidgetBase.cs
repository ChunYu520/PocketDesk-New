using Avalonia.Controls;
using PocketDesk.Shared;
using PocketDesk.Shared.Widgets;

namespace PocketDesk.Widgets;

/// <summary>
/// 组件基类，处理实例设置的读写与变更通知。组件可以只重写 <see cref="BuildView"/>。
/// </summary>
public abstract class WidgetBase : IWidget
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public virtual string Description => string.Empty;

    public virtual string Glyph => "\uE8B7";

    public virtual bool IsBuiltIn => false;

    public virtual bool SupportsMultipleInstances => true;

    public abstract WidgetSettingsSchema GetSettingsSchema();

    public Control CreateView(IWidgetContext context) => BuildView(context);

    protected abstract Control BuildView(IWidgetContext context);
}

/// <summary>设置 Schema 的流式构造辅助。</summary>
public sealed class WidgetSchemaBuilder
{
    private readonly WidgetSettingsSchema _schema;

    public WidgetSchemaBuilder(string widgetTypeId, string title, string? description = null)
    {
        _schema = new WidgetSettingsSchema
        {
            WidgetTypeId = widgetTypeId,
            Title = title,
            Description = description,
        };
    }

    public WidgetSchemaBuilder Text(string key, string label, string defaultValue = "", string? description = null, string? placeholder = null)
    {
        _schema.Fields.Add(new WidgetSettingField
        {
            Key = key,
            Label = label,
            Type = WidgetSettingTypes.Text,
            DefaultValue = defaultValue,
            Description = description,
            Placeholder = placeholder,
        });
        return this;
    }

    public WidgetSchemaBuilder Number(string key, string label, double defaultValue, double min, double max, double step = 1, string? description = null)
    {
        _schema.Fields.Add(new WidgetSettingField
        {
            Key = key,
            Label = label,
            Type = WidgetSettingTypes.Number,
            DefaultValue = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Min = min,
            Max = max,
            Step = step,
            Description = description,
        });
        return this;
    }

    public WidgetSchemaBuilder Toggle(string key, string label, bool defaultValue, string? description = null)
    {
        _schema.Fields.Add(new WidgetSettingField
        {
            Key = key,
            Label = label,
            Type = WidgetSettingTypes.Toggle,
            DefaultValue = defaultValue ? "true" : "false",
            Description = description,
        });
        return this;
    }

    public WidgetSchemaBuilder Choice(string key, string label, string defaultValue, string? description, params (string Value, string Label)[] choices)
    {
        _schema.Fields.Add(new WidgetSettingField
        {
            Key = key,
            Label = label,
            Type = WidgetSettingTypes.Choice,
            DefaultValue = defaultValue,
            Description = description,
            Choices = choices.Select(c => new WidgetSettingChoice(c.Value, c.Label)).ToList(),
        });
        return this;
    }

    public WidgetSchemaBuilder Color(string key, string label, string defaultValue, string? description = null)
    {
        _schema.Fields.Add(new WidgetSettingField
        {
            Key = key,
            Label = label,
            Type = WidgetSettingTypes.Color,
            DefaultValue = defaultValue,
            Description = description,
        });
        return this;
    }

    public WidgetSettingsSchema Build() => _schema;
}
