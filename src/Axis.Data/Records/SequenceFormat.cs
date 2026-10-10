using System.Globalization;
using System.Text.RegularExpressions;

namespace Axis.Data.Records;

/// <summary>
/// Turns a sequence's format and a number into the business number, such as
/// <c>PR-2026-00042</c> from <c>PR-{yyyy}-{n:5}</c>. The compiler has already checked the format.
/// </summary>
public static partial class SequenceFormat
{
    private const string YearToken = "{yyyy}";

    /// <summary>
    /// The period the counter runs in: <paramref name="utcYear"/> when the format holds
    /// <c>{yyyy}</c>, so the counter restarts each year, and 0 otherwise.
    /// </summary>
    public static int Period(string format, int utcYear)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.Contains(YearToken, StringComparison.Ordinal) ? utcYear : 0;
    }

    /// <summary>
    /// Replaces <c>{yyyy}</c> with <paramref name="utcYear"/>, <c>{n}</c> with
    /// <paramref name="number"/> and <c>{n:k}</c> with the number zero-padded to <c>k</c> digits.
    /// A number wider than <c>k</c> digits is kept whole.
    /// </summary>
    public static string Format(string format, int utcYear, long number)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Token().Replace(format, match =>
            match.Value == YearToken
                ? utcYear.ToString("D4", CultureInfo.InvariantCulture)
                : match.Groups["k"].Success
                    ? number.ToString("D" + match.Groups["k"].Value, CultureInfo.InvariantCulture)
                    : number.ToString(CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"\{yyyy\}|\{n(?::(?<k>[0-9]+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
