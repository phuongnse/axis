using System.Globalization;

namespace Axis.Data.Records;

/// <summary>
/// A JSON number as written, split into its digits and the position of the decimal point after
/// the exponent is applied. The digit counts are known without building any text, so a number is
/// checked against its limits first and only rendered when it fits them.
/// </summary>
/// <param name="Negative">Whether the number was written with a minus sign.</param>
/// <param name="Digits">The integer and fraction digits as written, without the point.</param>
/// <param name="Point">The number of digits before the decimal point once the exponent is applied; may be negative or beyond <see cref="Digits"/>.</param>
/// <param name="IntegerDigits">The integer digits without leading zeros.</param>
/// <param name="FractionDigits">The fraction digits without trailing zeros.</param>
internal readonly record struct JsonNumber(bool Negative, string Digits, long Point, long IntegerDigits, long FractionDigits)
{
    public bool IsZero => IntegerDigits == 0 && FractionDigits == 0;
}

internal static class JsonNumberText
{
    // A larger exponent saturates to this value, still far beyond every digit limit, which leaves
    // room to add the digit count without overflowing.
    private const long SaturatedExponent = long.MaxValue / 2;

    /// <summary>Splits the raw text of a JSON number, as <c>JsonElement.GetRawText</c> returns it.</summary>
    public static JsonNumber Parse(string raw)
    {
        var index = 0;
        var negative = raw[0] == '-';
        if (negative)
        {
            index++;
        }

        var integerStart = index;
        index = SkipDigits(raw, index);
        var integerPart = raw[integerStart..index];

        var fractionPart = "";
        if (index < raw.Length && raw[index] == '.')
        {
            var fractionStart = ++index;
            index = SkipDigits(raw, index);
            fractionPart = raw[fractionStart..index];
        }

        long exponent = 0;
        if (index < raw.Length && raw[index] is 'e' or 'E')
        {
            var text = raw.AsSpan(index + 1);
            exponent = long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var written)
                ? Math.Clamp(written, -SaturatedExponent, SaturatedExponent)
                : text[0] == '-' ? -SaturatedExponent : SaturatedExponent;
        }

        var digits = integerPart + fractionPart;
        var point = integerPart.Length + exponent;
        var first = digits.AsSpan().IndexOfAnyExcept('0');
        if (first < 0)
        {
            return new JsonNumber(negative, digits, point, 0, 0);
        }

        var last = digits.AsSpan().LastIndexOfAnyExcept('0');
        return new JsonNumber(negative, digits, point, Math.Max(0, point - first), Math.Max(0, last + 1 - point));
    }

    /// <summary>
    /// Renders the number in plain notation, keeping the written trailing zeros. Call it only for a
    /// number whose digit counts were checked against a limit.
    /// </summary>
    public static string Render(JsonNumber number)
    {
        var (integer, fraction) = Split(number);
        var text = fraction.Length == 0 ? integer : $"{integer}.{fraction}";
        return number.Negative && !number.IsZero ? $"-{text}" : text;
    }

    /// <summary>Renders the integer part with its sign. Call it only for a number with no fraction digits.</summary>
    public static string RenderInteger(JsonNumber number)
    {
        var (integer, _) = Split(number);
        return number.Negative && !number.IsZero ? $"-{integer}" : integer;
    }

    private static (string Integer, string Fraction) Split(JsonNumber number)
    {
        var digits = number.Digits;
        var point = number.Point;

        // Zero written with a far-away exponent would otherwise pad a huge run of zeros.
        if (number.IsZero && (point < 0 || point > digits.Length))
        {
            return ("0", "");
        }

        string integer;
        string fraction;
        if (point <= 0)
        {
            integer = "";
            fraction = new string('0', (int)-point) + digits;
        }
        else if (point >= digits.Length)
        {
            integer = digits + new string('0', (int)(point - digits.Length));
            fraction = "";
        }
        else
        {
            integer = digits[..(int)point];
            fraction = digits[(int)point..];
        }

        integer = integer.TrimStart('0');
        return (integer.Length == 0 ? "0" : integer, fraction);
    }

    private static int SkipDigits(string raw, int index)
    {
        while (index < raw.Length && char.IsAsciiDigit(raw[index]))
        {
            index++;
        }

        return index;
    }
}
