using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>
/// Converts record values to the CLR types the expression interpreter reads, listed on
/// <see cref="Axis.Expressions.Evaluation.ExpressionValues"/>. A decimal that <see cref="decimal"/>
/// cannot hold without rounding is reported rather than rounded.
/// </summary>
internal static class RecordClrValues
{
    /// <summary>
    /// Converts a parsed body value to the CLR type the interpreter reads. Every type but decimal
    /// already has it; a decimal is plain-notation text. <paramref name="exact"/> is false for a
    /// decimal that <see cref="decimal"/> cannot hold without rounding.
    /// </summary>
    public static object? FromInput(RecordValue value, out bool exact)
    {
        exact = true;
        if (value.Value is null || value.Field.Type != FieldType.Decimal)
        {
            return value.Value;
        }

        exact = TryParseDecimal((string)value.Value, out var number);
        return exact ? number : null;
    }

    /// <summary>
    /// Converts a stored value, as <see cref="RecordQueries"/> reads it, to the CLR type the
    /// interpreter reads. Returns false for a decimal that <see cref="decimal"/> cannot hold
    /// without rounding.
    /// </summary>
    public static bool TryFromStored(FieldModel field, JsonNode? node, out object? value)
    {
        value = null;
        if (node is null)
        {
            return true;
        }

        switch (field.Type)
        {
            case FieldType.Text or FieldType.Enum:
                value = node.GetValue<string>();
                return true;
            case FieldType.Integer:
                value = node.GetValue<long>();
                return true;
            case FieldType.Decimal:
                // The stored number text is kept as read, so every digit is still there.
                if (!TryParseDecimal(node.ToJsonString(), out var number))
                {
                    return false;
                }

                value = number;
                return true;
            case FieldType.Boolean:
                value = node.GetValue<bool>();
                return true;
            case FieldType.Date:
                value = DateOnly.ParseExact(node.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            case FieldType.DateTime:
                value = DateTimeOffset.ParseExact(
                    node.GetValue<string>(),
                    "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                return true;
            case FieldType.Reference:
                value = Guid.Parse(node.GetValue<string>());
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field.Type, "A field without a column has no stored value.");
        }
    }

    /// <summary>
    /// Parses plain-notation decimal text. <see cref="decimal"/> rounds digits it cannot hold, so
    /// the value counts only when it shows the same number as the text.
    /// </summary>
    public static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
        && Normalize(text) == Normalize(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Drops the trailing fraction zeros and the sign of zero, which do not change the number.</summary>
    private static string Normalize(string text)
    {
        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text is "-0" ? "0" : text;
    }
}
