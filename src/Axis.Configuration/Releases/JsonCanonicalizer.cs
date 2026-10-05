using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Axis.Configuration.Releases;

/// <summary>
/// Writes JSON in the canonical form of RFC 8785 (JCS): no whitespace, object properties sorted by
/// their UTF-16 code units, strings with minimal escaping and numbers formatted as ECMAScript does.
/// Two documents that differ only in formatting have the same canonical form.
/// </summary>
public static class JsonCanonicalizer
{
    public static string Canonicalize(JsonElement element)
    {
        var builder = new StringBuilder();
        Write(element, builder);
        return builder.ToString();
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(property.Name, builder);
                    builder.Append(':');
                    Write(property.Value, builder);
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        builder.Append(',');
                    }

                    Write(item, builder);
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, builder);
                break;
            case JsonValueKind.Number:
                if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
                {
                    throw new JsonException($"The number {element.GetRawText()} is outside the range of an IEEE 754 double.");
                }

                builder.Append(FormatNumber(number));
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            default:
                throw new ArgumentException($"Unexpected JSON value kind '{element.ValueKind}'.", nameof(element));
        }
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case < ' ':
                    builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
    }

    /// <summary>Formats a finite double as ECMAScript's <c>Number.prototype.toString</c> does.</summary>
    internal static string FormatNumber(double value)
    {
        if (value == 0)
        {
            // Covers negative zero, which ECMAScript also writes as "0".
            return "0";
        }

        // "R" gives the shortest digits that round-trip, as "d.dddE+xx" or as plain decimal notation.
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var pointIndex = text.IndexOf('.', StringComparison.Ordinal);
        var integerLength = pointIndex >= 0 ? pointIndex : text.Length;
        var digits = pointIndex >= 0 ? text.Remove(pointIndex, 1) : text;
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leadingZeros..].TrimEnd('0');

        // The value is 0.digits × 10^decimalExponent, with k significant digits.
        var decimalExponent = integerLength - leadingZeros + exponent;
        var k = digits.Length;
        var sign = value < 0 ? "-" : "";

        if (k <= decimalExponent && decimalExponent <= 21)
        {
            return sign + digits + new string('0', decimalExponent - k);
        }

        if (0 < decimalExponent && decimalExponent <= 21)
        {
            return sign + digits[..decimalExponent] + "." + digits[decimalExponent..];
        }

        if (-6 < decimalExponent && decimalExponent <= 0)
        {
            return sign + "0." + new string('0', -decimalExponent) + digits;
        }

        var shownExponent = decimalExponent - 1;
        var mantissa = k == 1 ? digits : digits[..1] + "." + digits[1..];
        return sign + mantissa + "e" + (shownExponent < 0 ? "-" : "+") + Math.Abs(shownExponent).ToString(CultureInfo.InvariantCulture);
    }
}
