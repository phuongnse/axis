namespace Axis.Configuration.Model;

/// <summary>Maps the widget type names used in page files to <see cref="WidgetType"/>.</summary>
public static class WidgetTypes
{
    private static readonly Dictionary<string, WidgetType> _typesByName = new(StringComparer.Ordinal)
    {
        ["table"] = WidgetType.Table,
        ["form"] = WidgetType.Form,
    };

    public static bool TryParse(string name, out WidgetType type) => _typesByName.TryGetValue(name, out type);

    public static WidgetType Parse(string name) =>
        TryParse(name, out var type) ? type : throw new ArgumentException($"Unknown widget type '{name}'.", nameof(name));
}
