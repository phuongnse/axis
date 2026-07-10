using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial record RuleParameterDefinition
{
    private RuleParameterDefinition(
        string key,
        RuleValueType type,
        bool isRequired,
        bool allowMultiple,
        IReadOnlyList<string> allowedValues)
    {
        Key = key;
        Type = type;
        IsRequired = isRequired;
        AllowMultiple = allowMultiple;
        AllowedValues = allowedValues;
    }

    public string Key { get; }
    public RuleValueType Type { get; }
    public bool IsRequired { get; }
    public bool AllowMultiple { get; }
    public IReadOnlyList<string> AllowedValues { get; }

    public static Result<RuleParameterDefinition> Create(
        string key,
        RuleValueType type,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null)
    {
        string normalizedKey = key?.Trim() ?? string.Empty;
        if (!ParameterKeyPattern().IsMatch(normalizedKey))
            return Result.Failure<RuleParameterDefinition>("Rule parameter key format is invalid.");

        if (!Enum.IsDefined(type))
            return Result.Failure<RuleParameterDefinition>("Rule parameter type is not supported.");

        List<string> normalizedAllowedValues = [];
        foreach (string allowedValue in allowedValues ?? [])
        {
            Result<RuleValue> normalized = RuleValue.Create(type, [allowedValue]);
            if (normalized.IsFailure)
                return Result.Failure<RuleParameterDefinition>(normalized.Error);

            normalizedAllowedValues.Add(normalized.Value.Values[0]);
        }

        if (normalizedAllowedValues.Count != normalizedAllowedValues.Distinct(StringComparer.Ordinal).Count())
            return Result.Failure<RuleParameterDefinition>("Rule parameter allowed values must be unique.");

        return new RuleParameterDefinition(
            normalizedKey,
            type,
            isRequired,
            allowMultiple,
            normalizedAllowedValues.AsReadOnly());
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterKeyPattern();
}
