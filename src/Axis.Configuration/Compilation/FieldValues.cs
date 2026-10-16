using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;

namespace Axis.Configuration.Compilation;

/// <summary>Reads JSON values as values of a field type, in the record API's JSON forms.</summary>
internal static class FieldValues
{
    /// <summary>
    /// Reads the JSON <paramref name="element"/> as a value of <paramref name="type"/>: a number for
    /// integer and decimal, true or false for boolean and a string for the other types. An enum value
    /// must be one of <paramref name="enumValues"/> and a reference is a UUID. Null and child
    /// collections are not values of any type here, so the caller decides what they mean.
    /// </summary>
    public static bool TryRead(FieldType type, IReadOnlyList<string>? enumValues, JsonElement element, out object? value)
    {
        value = null;
        switch (type)
        {
            case FieldType.Text when element.ValueKind == JsonValueKind.String && element.GetString() is { } text && !text.Contains('\0', StringComparison.Ordinal):
                value = text;
                return true;
            case FieldType.Integer when element.ValueKind == JsonValueKind.Number
                && element.TryGetDecimal(out var whole)
                && decimal.Truncate(whole) == whole
                && whole >= long.MinValue
                && whole <= long.MaxValue:
                value = (long)whole;
                return true;
            case FieldType.Decimal when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number):
                value = number;
                return true;
            case FieldType.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case FieldType.Date when element.ValueKind == JsonValueKind.String && DateLiterals.TryParseDate(element.GetString()!, out var date):
                value = date;
                return true;
            case FieldType.DateTime when element.ValueKind == JsonValueKind.String && DateLiterals.TryParseDateTime(element.GetString()!, out var dateTime):
                value = dateTime.ToUniversalTime();
                return true;
            case FieldType.Enum when element.ValueKind == JsonValueKind.String
                && enumValues?.Contains(element.GetString()!, StringComparer.Ordinal) == true:
                value = element.GetString();
                return true;
            case FieldType.Reference when element.ValueKind == JsonValueKind.String
                && element.GetString() is { Length: 36 } id
                && Guid.TryParseExact(id, "D", out var guid):
                value = guid;
                return true;
            default:
                return false;
        }
    }
}
