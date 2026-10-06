namespace Axis.Configuration.Model;

/// <summary>Maps the widget type names used in page files to <see cref="WidgetType"/>.</summary>
public static class WidgetTypes
{
    private static readonly Dictionary<string, WidgetType> _typesByName = new(StringComparer.Ordinal)
    {
        ["table"] = WidgetType.Table,
        ["form"] = WidgetType.Form,
    };

    private static readonly Dictionary<WidgetType, string> _namesByType = _typesByName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool TryParse(string name, out WidgetType type) => _typesByName.TryGetValue(name, out type);

    public static WidgetType Parse(string name) =>
        TryParse(name, out var type) ? type : throw new ArgumentException($"Unknown widget type '{name}'.", nameof(name));

    /// <summary>The name of <paramref name="type"/> as written in configuration files.</summary>
    public static string Name(WidgetType type) =>
        _namesByType.TryGetValue(type, out var name) ? name : throw new ArgumentOutOfRangeException(nameof(type), type, null);
}
