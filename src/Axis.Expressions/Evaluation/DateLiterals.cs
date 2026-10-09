using System.Globalization;
using System.Text.RegularExpressions;

namespace Axis.Expressions.Evaluation;

/// <summary>
/// Reads the text of <c>date('…')</c> and <c>dateTime('…')</c>, and of date and date-time data
/// source parameters in a query string. The rules are the record API's: a
/// date is <c>yyyy-MM-dd</c>, and a date-time is RFC 3339 with an offset, up to 6 fraction digits,
/// no leap second, an offset of at most 14 hours, and an instant inside 0001 to 9999 in UTC.
/// </summary>
public static partial class DateLiterals
{
    private const string DateFormat = "yyyy-MM-dd";
    private const int TickDigits = 7;
    private static readonly TimeSpan _maxOffset = TimeSpan.FromHours(14);

    public static bool TryParseDate(string text, out DateOnly value)
    {
        value = default;
        return DatePattern().IsMatch(text)
            && DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    public static bool TryParseDateTime(string text, out DateTimeOffset value)
    {
        value = default;
        if (DateTimePattern().Match(text) is not { Success: true } match
            || !DateOnly.TryParseExact(match.Groups["date"].ValueSpan, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        var hour = ReadDigits(match.Groups["hour"]);
        var minute = ReadDigits(match.Groups["minute"]);
        var second = ReadDigits(match.Groups["second"]);
        if (hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        var offset = TimeSpan.Zero;
        if (match.Groups["sign"].Success)
        {
            var offsetMinutes = ReadDigits(match.Groups["offsetMinute"]);
            offset = new TimeSpan(ReadDigits(match.Groups["offsetHour"]), offsetMinutes, 0);
            if (offsetMinutes > 59 || offset > _maxOffset)
            {
                return false;
            }

            if (match.Groups["sign"].ValueSpan[0] == '-')
            {
                offset = -offset;
            }
        }

        long fractionTicks = 0;
        if (match.Groups["fraction"] is { Success: true } fraction)
        {
            fractionTicks = ReadDigits(fraction);
            for (var digits = fraction.Length; digits < TickDigits; digits++)
            {
                fractionTicks *= 10;
            }
        }

        var localTicks = date.ToDateTime(new TimeOnly(hour, minute, second)).Ticks + fractionTicks;
        try
        {
            value = new DateTimeOffset(localTicks, offset).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // The instant falls outside 0001..9999 in UTC.
            return false;
        }

        return true;
    }

    private static int ReadDigits(Group group) => int.Parse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);

    // [0-9] rather than \d, which also matches non-ASCII digits; \z rather than $, which allows a final newline.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(
        @"^(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})[Tt](?<hour>[0-9]{2}):(?<minute>[0-9]{2}):(?<second>[0-9]{2})(?:\.(?<fraction>[0-9]{1,6}))?(?:[Zz]|(?<sign>[+-])(?<offsetHour>[0-9]{2}):(?<offsetMinute>[0-9]{2}))\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex DateTimePattern();
}
