using System.Globalization;
using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial record RuleValue
{
    private const int MaxValueCount = 1000;
    private const int MaxTextLength = 4000;

    private RuleValue(RuleValueType type, IReadOnlyList<string> values)
    {
        Type = type;
        Values = values;
    }

    public RuleValueType Type { get; }
    public IReadOnlyList<string> Values { get; }
    public bool IsMultiple => Values.Count > 1;

    public static Result<RuleValue> Create(
        RuleValueType type,
        IEnumerable<string>? values,
        bool allowMultiple = false)
    {
        if (!Enum.IsDefined(type))
            return Result.Failure<RuleValue>("Rule value type is not supported.");

        string[] source = values?.ToArray() ?? [];
        if (source.Length == 0)
            return Result.Failure<RuleValue>("Rule value is required.");

        if (!allowMultiple && source.Length != 1)
            return Result.Failure<RuleValue>("Rule value must contain exactly one value.");

        if (source.Length > MaxValueCount)
            return Result.Failure<RuleValue>("Rule value contains too many values.");

        List<string> normalized = [];
        foreach (string sourceValue in source)
        {
            Result<string> value = Normalize(type, sourceValue);
            if (value.IsFailure)
                return Result.Failure<RuleValue>(value.Error);

            normalized.Add(value.Value);
        }

        return new RuleValue(type, normalized.AsReadOnly());
    }

    private static Result<string> Normalize(RuleValueType type, string? source)
    {
        string value = source ?? string.Empty;
        return type switch
        {
            RuleValueType.Text => NormalizeText(value),
            RuleValueType.Integer => NormalizeInteger(value),
            RuleValueType.Decimal => NormalizeDecimal(value),
            RuleValueType.Date => NormalizeDate(value),
            RuleValueType.DateTime => NormalizeDateTime(value),
            RuleValueType.Boolean => NormalizeBoolean(value),
            _ => Result.Failure<string>("Rule value type is not supported."),
        };
    }

    private static Result<string> NormalizeText(string value) =>
        value.Length > MaxTextLength
            ? Result.Failure<string>("Text rule value is too long.")
            : value;

    private static Result<string> NormalizeInteger(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : Result.Failure<string>("Integer rule value is invalid.");

    private static Result<string> NormalizeDecimal(string value) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : Result.Failure<string>("Decimal rule value is invalid.");

    private static Result<string> NormalizeDate(string value) =>
        DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : Result.Failure<string>("Date rule value must use yyyy-MM-dd.");

    private static Result<string> NormalizeDateTime(string value)
    {
        string normalized = value.Trim();
        if (!ExplicitOffsetPattern().IsMatch(normalized))
            return Result.Failure<string>("DateTime rule value requires an explicit offset.");

        return DateTimeOffset.TryParse(
            normalized,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : Result.Failure<string>("DateTime rule value is invalid.");
    }

    private static Result<string> NormalizeBoolean(string value) =>
        bool.TryParse(value.Trim(), out bool parsed)
            ? parsed.ToString().ToLowerInvariant()
            : Result.Failure<string>("Boolean rule value is invalid.");

    [GeneratedRegex("(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffsetPattern();
}
