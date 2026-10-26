using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Processes.Instances;

/// <summary>
/// Formats the stored placeholder values of a notification for its reader. The backend runs with invariant
/// globalization, so it cannot load the data of a locale. Two built-in conventions stand in: <c>vi</c> for a locale
/// whose language is <c>vi</c>, and <c>en</c> for every other locale. The language is the part of the locale before
/// the first <c>-</c> or <c>_</c>, ignoring letter case.
/// <list type="bullet">
/// <item><c>en</c> writes <c>1,234.5</c>, <c>Jan 1, 2027</c> and <c>Jan 1, 2027, 1:00 AM</c>.</item>
/// <item><c>vi</c> writes <c>1.234,5</c>, <c>01/01/2027</c> and <c>01/01/2027 01:00</c>.</item>
/// </list>
/// Every digit of a number is kept. A <c>date-time</c> shows in the given time zone. A <c>date</c> shows as the local
/// date it is, with no shift. A value of any other type, or one of a notification that stored no types, shows as it
/// was stored: a string as it is, a boolean as JSON writes it and null as empty text. A number is formatted even
/// without a type. Text that is not the date or date-time its type says stays as it is.
/// </summary>
public static class NotificationValues
{
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly NumberFormatInfo _englishNumbers = Numbers(group: ",", decimalSeparator: ".");

    private static readonly NumberFormatInfo _vietnameseNumbers = Numbers(group: ".", decimalSeparator: ",");

    /// <summary>
    /// The text of each value in <paramref name="valuesJson"/>, a JSON object, by its name. <paramref name="valueTypesJson"/>
    /// is the JSON object of type names stored with it. <paramref name="locale"/> picks the conventions, and a null one
    /// uses <c>en</c>. <paramref name="timeZone"/> is the time zone that a <c>date-time</c> shows in.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Format(string valuesJson, string valueTypesJson, string? locale, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(valuesJson);
        ArgumentNullException.ThrowIfNull(valueTypesJson);
        ArgumentNullException.ThrowIfNull(timeZone);

        var vietnamese = IsVietnamese(locale);
        var types = JsonNode.Parse(valueTypesJson) as JsonObject;
        var formatted = new Dictionary<string, string>(StringComparer.Ordinal);
        if (JsonNode.Parse(valuesJson) is JsonObject stored)
        {
            foreach (var (name, value) in stored)
            {
                var type = types?[name] is JsonValue typeValue && typeValue.TryGetValue<string>(out var typeName) ? typeName : null;
                formatted[name] = FormatValue(value, type, vietnamese, timeZone);
            }
        }

        return formatted;
    }

    private static string FormatValue(JsonNode? value, string? type, bool vietnamese, TimeZoneInfo timeZone)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is JsonValue json)
        {
            if (json.TryGetValue<string>(out var text))
            {
                return type switch
                {
                    "date-time" => FormatDateTime(text, vietnamese, timeZone) ?? text,
                    "date" => FormatDate(text, vietnamese) ?? text,
                    _ => text,
                };
            }

            if (json.GetValueKind() == JsonValueKind.Number)
            {
                var raw = json.ToJsonString();
                return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("N" + number.Scale.ToString(CultureInfo.InvariantCulture), vietnamese ? _vietnameseNumbers : _englishNumbers)
                    : raw;
            }
        }

        return value.ToJsonString();
    }

    private static string? FormatDateTime(string text, bool vietnamese, TimeZoneInfo timeZone)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(time, timeZone);
        return local.ToString(vietnamese ? "dd/MM/yyyy HH:mm" : "MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture);
    }

    private static string? FormatDate(string text, bool vietnamese) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString(vietnamese ? "dd/MM/yyyy" : "MMM d, yyyy", CultureInfo.InvariantCulture)
            : null;

    private static bool IsVietnamese(string? locale)
    {
        if (locale is null)
        {
            return false;
        }

        var end = locale.AsSpan().IndexOfAny('-', '_');
        var language = end < 0 ? locale.AsSpan() : locale.AsSpan(0, end);
        return language.Equals("vi", StringComparison.OrdinalIgnoreCase);
    }

    private static NumberFormatInfo Numbers(string group, string decimalSeparator)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSeparator = group;
        format.NumberDecimalSeparator = decimalSeparator;
        return format;
    }
}
