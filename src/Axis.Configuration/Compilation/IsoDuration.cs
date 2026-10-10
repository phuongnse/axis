using System.Globalization;
using System.Text.RegularExpressions;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Parses the ISO 8601 durations a task's <c>dueIn</c> accepts: whole-number weeks, such as
/// <c>P2W</c>, or days with an optional time part of hours, minutes and seconds, such as
/// <c>P3D</c>, <c>PT4H</c> or <c>P1DT12H</c>. Years and months have no fixed length, so they are
/// refused.
/// </summary>
internal static partial class IsoDuration
{
    /// <summary>
    /// Parses <paramref name="text"/> into a fixed duration. Returns false when it does not have the
    /// accepted form, does not fit a <see cref="TimeSpan"/>, or is not greater than zero.
    /// </summary>
    public static bool TryParse(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = Pattern().Match(text);
        if (!match.Success
            || !TryPart(match, "w", out var weeks) || !TryPart(match, "d", out var days) || !TryPart(match, "h", out var hours)
            || !TryPart(match, "m", out var minutes) || !TryPart(match, "s", out var seconds))
        {
            return false;
        }

        try
        {
            var totalHours = checked((((weeks * 7) + days) * 24) + hours);
            var totalSeconds = checked((((totalHours * 60) + minutes) * 60) + seconds);
            duration = TimeSpan.FromSeconds(totalSeconds);
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            duration = TimeSpan.Zero;
            return false;
        }

        return duration > TimeSpan.Zero;
    }

    /// <summary>Reads a part that may be absent, which counts as zero.</summary>
    private static bool TryPart(Match match, string name, out long value)
    {
        value = 0;
        var group = match.Groups[name];
        return !group.Success || long.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // At least one part follows "P", and a digit follows "T", so a bare "P" or a trailing "T" is refused.
    [GeneratedRegex(
        @"^P(?=.)(?:(?<w>[0-9]+)W|(?:(?<d>[0-9]+)D)?(?:T(?=[0-9])(?:(?<h>[0-9]+)H)?(?:(?<m>[0-9]+)M)?(?:(?<s>[0-9]+)S)?)?)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
