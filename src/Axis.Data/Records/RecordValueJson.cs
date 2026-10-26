using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>Shows a computed or defaulted <see cref="RecordValue"/> as the record API does.</summary>
internal static class RecordValueJson
{
    private const string DateFormat = "yyyy-MM-dd";

    // PostgreSQL stores microseconds, so six fraction digits show every stored value.
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>The value as <c>RecordQueries.ReadValue</c> gives it for a stored column, in the record API's value format.</summary>
    public static JsonNode? ToJson(RecordValue value)
    {
        if (value.Value is null)
        {
            return null;
        }

        return value.Field.Type switch
        {
            FieldType.Text or FieldType.Enum => JsonValue.Create((string)value.Value),
            FieldType.Integer => JsonValue.Create((long)value.Value),
            FieldType.Decimal or FieldType.Money => JsonNode.Parse(DecimalText(value.Field, (string)value.Value)),
            FieldType.Boolean => JsonValue.Create((bool)value.Value),
            FieldType.Date => JsonValue.Create(((DateOnly)value.Value).ToString(DateFormat, CultureInfo.InvariantCulture)),
            FieldType.DateTime => JsonValue.Create(((DateTimeOffset)value.Value).UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
            FieldType.Reference => JsonValue.Create(((Guid)value.Value).ToString("D")),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Field.Type, "A field of this type cannot be computed."),
        };
    }

    // A column with a precision shows exactly its scale in fraction digits, as numeric(p,s)::text does. Otherwise
    // the text stays as computed.
    private static string DecimalText(FieldModel field, string text)
    {
        if (field.Precision is null)
        {
            return text;
        }

        var scale = field.Scale ?? 0;
        var negative = text.StartsWith('-');
        var unsigned = negative ? text[1..] : text;
        var point = unsigned.IndexOf('.', StringComparison.Ordinal);
        var integer = point < 0 ? unsigned : unsigned[..point];
        var fraction = (point < 0 ? "" : unsigned[(point + 1)..]).TrimEnd('0').PadRight(scale, '0');
        var number = scale == 0 ? integer : $"{integer}.{fraction}";
        return negative && number.Any(character => character is >= '1' and <= '9') ? "-" + number : number;
    }
}
