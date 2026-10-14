using System.Diagnostics.CodeAnalysis;

namespace Axis.Configuration.Model;

/// <summary>
/// A 5-field cron: minute, hour, day of month, month and day of week. A field is a comma list of
/// items, and an item is <c>*</c>, a number or a range, optionally followed by <c>/step</c> after
/// <c>*</c> or a range. Day of week is 0 to 7, and 0 and 7 are both Sunday. When both day of month
/// and day of week are restricted, a day matches if either matches. A field that starts with
/// <c>*</c> is unrestricted.
/// </summary>
public sealed class CronSchedule
{
    private const int MaxYearsAhead = 30;

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _daysOfMonth;
    private readonly bool[] _months;
    private readonly bool[] _daysOfWeek;
    private readonly bool _dayOfMonthRestricted;
    private readonly bool _dayOfWeekRestricted;

    private CronSchedule(
        string expression,
        bool[] minutes,
        bool[] hours,
        bool[] daysOfMonth,
        bool[] months,
        bool[] daysOfWeek,
        bool dayOfMonthRestricted,
        bool dayOfWeekRestricted)
    {
        Expression = expression;
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _dayOfMonthRestricted = dayOfMonthRestricted;
        _dayOfWeekRestricted = dayOfWeekRestricted;
    }

    /// <summary>The cron text as written in the trigger file.</summary>
    public string Expression { get; }

    /// <summary>Parses <paramref name="text"/>. When it is not a valid cron, <paramref name="error"/> says why.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out CronSchedule? schedule, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);

        schedule = null;
        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            error = $"The schedule has {fields.Length} fields, but a cron has 5: minute, hour, day of month, month and day of week.";
            return false;
        }

        var minutes = ParseField(fields[0], "minute", 0, 59, out error);
        var hours = minutes is null ? null : ParseField(fields[1], "hour", 0, 23, out error);
        var daysOfMonth = hours is null ? null : ParseField(fields[2], "day of month", 1, 31, out error);
        var months = daysOfMonth is null ? null : ParseField(fields[3], "month", 1, 12, out error);
        var daysOfWeek = months is null ? null : ParseField(fields[4], "day of week", 0, 7, out error);
        if (daysOfWeek is null)
        {
            error ??= "The schedule is not valid.";
            return false;
        }

        // 7 is Sunday, like 0.
        if (daysOfWeek[7])
        {
            daysOfWeek[0] = true;
        }

        var dayOfMonthRestricted = !fields[2].StartsWith('*');
        var dayOfWeekRestricted = !fields[4].StartsWith('*');

        // Only a restricted day of month with an unrestricted day of week can leave no date at all.
        // February counts as 29 days, so a leap year day is not never.
        if (dayOfMonthRestricted && !dayOfWeekRestricted && !HasDate(daysOfMonth!, months!))
        {
            error = "The schedule never matches a date. None of its months has one of its days of month.";
            return false;
        }

        error = null;
        schedule = new CronSchedule(
            string.Join(' ', fields), minutes!, hours!, daysOfMonth!, months!, daysOfWeek, dayOfMonthRestricted, dayOfWeekRestricted);
        return true;
    }

    /// <summary>
    /// The first occurrence after <paramref name="instant"/> in <paramref name="timeZone"/>, in UTC. A
    /// local time that does not exist runs at the next valid local time. A repeated local time runs
    /// once, at its first instant.
    /// </summary>
    public DateTimeOffset NextAfter(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(instant, timeZone).DateTime;
        var candidate = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        var limit = candidate.AddYears(MaxYearsAhead);
        while (candidate < limit)
        {
            if (!_months[candidate.Month])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1).AddMonths(1);
                continue;
            }

            if (!DayMatches(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!_hours[candidate.Hour])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0).AddHours(1);
                continue;
            }

            if (!_minutes[candidate.Minute])
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            var occurrence = ToInstant(candidate, timeZone);
            if (occurrence > instant)
            {
                return occurrence;
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException($"The schedule '{Expression}' has no occurrence in the {MaxYearsAhead} years after {instant:O}.");
    }

    /// <summary>The UTC instant that local time <paramref name="local"/> runs at.</summary>
    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(local))
        {
            // The clock skips this time. Run at the first valid local time after it.
            var valid = local;
            while (timeZone.IsInvalidTime(valid))
            {
                valid = valid.AddMinutes(1);
            }

            return new DateTimeOffset(DateTime.SpecifyKind(valid - timeZone.GetUtcOffset(valid), DateTimeKind.Utc));
        }

        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(DateTime.SpecifyKind(local - offset, DateTimeKind.Utc));
    }

    private bool DayMatches(DateTime date)
    {
        var dayOfMonth = _daysOfMonth[date.Day];
        var dayOfWeek = _daysOfWeek[(int)date.DayOfWeek];
        return _dayOfMonthRestricted && _dayOfWeekRestricted ? dayOfMonth || dayOfWeek : dayOfMonth && dayOfWeek;
    }

    private static bool HasDate(bool[] daysOfMonth, bool[] months)
    {
        for (var month = 1; month <= 12; month++)
        {
            if (!months[month])
            {
                continue;
            }

            var days = DateTime.DaysInMonth(2024, month);
            for (var day = 1; day <= days; day++)
            {
                if (daysOfMonth[day])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Parses one field into a table indexed by value, or returns null with <paramref name="error"/>.</summary>
    private static bool[]? ParseField(string field, string name, int min, int max, out string? error)
    {
        var allowed = new bool[max + 1];
        foreach (var item in field.Split(','))
        {
            var step = 1;
            var range = item;
            var slash = item.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                range = item[..slash];
                if (!TryReadNumber(item[(slash + 1)..], out step) || step < 1)
                {
                    error = $"The {name} field has the step '{item[(slash + 1)..]}' in '{item}'. A step is a whole number of at least 1.";
                    return null;
                }
            }

            int from;
            int to;
            if (range == "*")
            {
                (from, to) = (min, max);
            }
            else if (range.Contains('-', StringComparison.Ordinal))
            {
                var bounds = range.Split('-');
                if (bounds.Length != 2 || !TryReadNumber(bounds[0], out from) || !TryReadNumber(bounds[1], out to))
                {
                    error = $"The {name} field has '{item}', which is not a range of two numbers.";
                    return null;
                }

                if (from > to)
                {
                    error = $"The {name} field has the range '{item}', which ends before it starts.";
                    return null;
                }
            }
            else if (slash >= 0)
            {
                error = $"The {name} field has '{item}'. A step follows '*' or a range, not a single number.";
                return null;
            }
            else if (TryReadNumber(range, out from))
            {
                to = from;
            }
            else
            {
                error = $"The {name} field has '{item}', which is not '*', a number or a range.";
                return null;
            }

            if (from < min || to > max)
            {
                error = $"The {name} field has '{item}', which is outside {min} to {max}.";
                return null;
            }

            for (var value = from; value <= to; value += step)
            {
                allowed[value] = true;
            }
        }

        error = null;
        return allowed;
    }

    /// <summary>Reads ASCII digits only. A longer number than <see cref="int"/> holds is not a number here.</summary>
    private static bool TryReadNumber(string text, out int value)
    {
        value = 0;
        if (text.Length is 0 or > 4)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }

            value = (value * 10) + (character - '0');
        }

        return true;
    }
}
