using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial record FieldRuleParameterDefinition(
    string Key,
    FieldRuleParameterType Type,
    bool IsRequired,
    bool AllowMultiple)
{
    public static Result<FieldRuleParameterDefinition> Create(
        string key,
        FieldRuleParameterType type,
        bool isRequired,
        bool allowMultiple)
    {
        string normalizedKey = key.Trim();
        if (normalizedKey.Length == 0)
            return Result.Failure<FieldRuleParameterDefinition>("Rule parameter key is required.");

        if (!ParameterKeyPattern().IsMatch(normalizedKey))
            return Result.Failure<FieldRuleParameterDefinition>("Rule parameter key format is invalid.");

        if (!Enum.IsDefined(type))
            return Result.Failure<FieldRuleParameterDefinition>("Rule parameter type is not supported.");

        return new FieldRuleParameterDefinition(normalizedKey, type, isRequired, allowMultiple);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterKeyPattern();
}
