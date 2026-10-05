using System.Globalization;
using Axis.Configuration.Model;

namespace Axis.Data.Schema;

/// <summary>
/// The PostgreSQL column type of each field type, spelled as <c>format_type</c> renders it, so a
/// catalog column matches the expected type by string equality.
/// </summary>
public static class ColumnTypes
{
    public const string Text = "text";

    private const string VaryingPrefix = "character varying(";

    public static string Render(FieldModel field) =>
        field.Type switch
        {
            FieldType.Text => field.MaxLength is { } maxLength ? $"{VaryingPrefix}{maxLength})" : Text,
            FieldType.Integer => "bigint",
            FieldType.Decimal => field.Precision is { } precision ? $"numeric({precision},{field.Scale ?? 0})" : "numeric",
            FieldType.Boolean => "boolean",
            FieldType.Date => "date",
            FieldType.DateTime => "timestamp with time zone",
            // Enum values are recorded by the planner, not enforced by a CHECK constraint.
            FieldType.Enum => Text,
            FieldType.Reference => "uuid",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
        };

    /// <summary>Reads the length of a <c>character varying(n)</c> type.</summary>
    public static bool TryParseVarying(string type, out int length)
    {
        length = 0;
        return type.StartsWith(VaryingPrefix, StringComparison.Ordinal)
            && type.EndsWith(')')
            && int.TryParse(type.AsSpan(VaryingPrefix.Length, type.Length - VaryingPrefix.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out length);
    }
}
