using System.Globalization;
using System.Text.RegularExpressions;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;

namespace Axis.Data.DataSources;

/// <summary>
/// The value of a data source parameter for one request, or <see langword="null"/> when it was not
/// given. The value has the CLR type of the parameter's type, as <see cref="DataSourceParameterValues.TryParse"/> gives it.
/// </summary>
public sealed record DataSourceParameterValue(DataSourceParameterModel Parameter, object? Value);

/// <summary>
/// Reads the plain text form of a data source parameter from a query string, as data-sources.md
/// lists it. Every rule is ordinal and culture invariant.
/// </summary>
public static partial class DataSourceParameterValues
{
    /// <summary>
    /// Parses <paramref name="text"/> as a value of <paramref name="parameter"/>'s type:
    /// <see cref="string"/> for text and enum, <see cref="long"/>, <see cref="decimal"/>,
    /// <see cref="bool"/>, <see cref="DateOnly"/>, <see cref="DateTimeOffset"/> in UTC and
    /// <see cref="Guid"/> for reference.
    /// </summary>
    public static bool TryParse(DataSourceParameterModel parameter, string text, out object? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(text);

        value = null;
        switch (parameter.Type)
        {
            case FieldType.Text when !text.Contains('\0', StringComparison.Ordinal):
                value = text;
                return true;
            case FieldType.Integer when IntegerPattern().IsMatch(text)
                && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer):
                value = integer;
                return true;
            case FieldType.Decimal when DecimalPattern().IsMatch(text)
                && decimal.TryParse(
                    text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number):
                value = number;
                return true;
            case FieldType.Boolean when text is "true" or "false":
                value = text == "true";
                return true;
            case FieldType.Date when DateLiterals.TryParseDate(text, out var date):
                value = date;
                return true;
            case FieldType.DateTime when DateLiterals.TryParseDateTime(text, out var dateTime):
                value = dateTime;
                return true;
            case FieldType.Enum when parameter.Values?.Contains(text, StringComparer.Ordinal) == true:
                value = text;
                return true;
            case FieldType.Reference when text.Length == 36 && Guid.TryParseExact(text, "D", out var id):
                value = id;
                return true;
            default:
                return false;
        }
    }

    // [0-9] rather than \d, which also matches non-ASCII digits; \z rather than $, which allows a final newline.
    [GeneratedRegex(@"^-?[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();
}
