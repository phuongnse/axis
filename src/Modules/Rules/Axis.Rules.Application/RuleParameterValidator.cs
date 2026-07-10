using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application;

internal static class RuleParameterValidator
{
    public static Result<IReadOnlyDictionary<string, RuleValue>> Validate(
        IReadOnlyList<RuleParameterDefinition> definitions,
        IReadOnlyDictionary<string, RuleValueDto> parameters)
    {
        Dictionary<string, RuleParameterDefinition> schema = definitions
            .ToDictionary(parameter => parameter.Key, StringComparer.Ordinal);
        Dictionary<string, RuleValue> canonical = new(StringComparer.Ordinal);

        foreach ((string rawKey, RuleValueDto valueDto) in parameters)
        {
            string key = rawKey.Trim();
            if (!schema.TryGetValue(key, out RuleParameterDefinition? parameter))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameter is not supported.");

            Result<RuleValue> value = RuleContractMapper.ToDomain(valueDto, parameter.AllowMultiple);
            if (value.IsFailure || value.Value.Type != parameter.Type)
            {
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>(
                    value.IsFailure ? value.Error : "Rule parameter type is invalid.");
            }

            if (parameter.AllowedValues.Count > 0 &&
                value.Value.Values.Any(candidate =>
                    !parameter.AllowedValues.Contains(candidate, StringComparer.Ordinal)))
            {
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameter value is not allowed.");
            }

            canonical[key] = value.Value;
        }

        foreach (RuleParameterDefinition parameter in definitions.Where(parameter => parameter.IsRequired))
        {
            if (!canonical.ContainsKey(parameter.Key))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule parameter is required.");
        }

        return canonical;
    }
}
