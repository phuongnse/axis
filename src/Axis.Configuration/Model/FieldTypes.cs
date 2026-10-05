namespace Axis.Configuration.Model;

/// <summary>Maps the field type names used in entity files to <see cref="FieldType"/>.</summary>
public static class FieldTypes
{
    private static readonly Dictionary<string, FieldType> _typesByName = new(StringComparer.Ordinal)
    {
        ["text"] = FieldType.Text,
        ["integer"] = FieldType.Integer,
        ["decimal"] = FieldType.Decimal,
        ["boolean"] = FieldType.Boolean,
        ["date"] = FieldType.Date,
        ["date-time"] = FieldType.DateTime,
        ["enum"] = FieldType.Enum,
        ["reference"] = FieldType.Reference,
    };

    public static bool TryParse(string name, out FieldType type) => _typesByName.TryGetValue(name, out type);

    public static FieldType Parse(string name) =>
        TryParse(name, out var type) ? type : throw new ArgumentException($"Unknown field type '{name}'.", nameof(name));
}
