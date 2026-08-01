using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

internal static class RuleInputValidator
{
    public static Result<IReadOnlyDictionary<string, RuleValue>> Validate(
        IReadOnlyList<RuleInputDefinition> definitions,
        IReadOnlyDictionary<string, RuleValueDto> inputs)
    {
        Dictionary<string, RuleInputDefinition> schema = definitions
            .ToDictionary(input => input.Key, StringComparer.Ordinal);
        Dictionary<string, RuleValue> canonical = new(StringComparer.Ordinal);

        foreach ((string rawKey, RuleValueDto valueDto) in inputs ?? new Dictionary<string, RuleValueDto>())
        {
            string key = rawKey?.Trim() ?? string.Empty;
            if (!schema.TryGetValue(key, out RuleInputDefinition? input))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input is not supported.");

            if (canonical.ContainsKey(key) || valueDto is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input keys must be unique.");

            if (!input.Types.Contains((DomainValueType)valueDto.Type))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input type is invalid.");

            Result<RuleValue> value = RuleContractMapper.ToDomain(valueDto, input.AllowMultiple);
            if (value.IsFailure)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>(value.Error);

            Result allowed = ValidateAllowedValues(input, value.Value);
            if (allowed.IsFailure)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>(allowed.Error);

            canonical[key] = value.Value;
        }

        foreach (RuleInputDefinition input in definitions.Where(input => input.IsRequired))
        {
            if (!canonical.ContainsKey(input.Key))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input is required.");
        }

        return canonical;
    }

    public static Result<IReadOnlyDictionary<string, RuleValue>> ValidateRaw(
        IReadOnlyList<RuleInputDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> inputs,
        IReadOnlyDictionary<string, DomainValueType>? preferredTypes = null)
    {
        Dictionary<string, RuleValueDto> typed = new(StringComparer.Ordinal);
        Dictionary<string, RuleInputDefinition> schema = definitions
            .ToDictionary(input => input.Key, StringComparer.Ordinal);

        foreach ((string rawKey, IReadOnlyList<string> values) in inputs ??
                 new Dictionary<string, IReadOnlyList<string>>())
        {
            string key = rawKey?.Trim() ?? string.Empty;
            if (!schema.TryGetValue(key, out RuleInputDefinition? input) || values is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input is invalid.");
            if (typed.ContainsKey(key))
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input keys must be unique.");

            RuleValue? matched = null;
            IEnumerable<DomainValueType> candidateTypes = preferredTypes is not null &&
                preferredTypes.TryGetValue(key, out DomainValueType preferredType)
                ? [preferredType]
                : input.Types;
            foreach (DomainValueType type in candidateTypes)
            {
                Result<RuleValue> candidate = RuleValue.Create(type, values, input.AllowMultiple);
                if (candidate.IsSuccess)
                {
                    matched = candidate.Value;
                    break;
                }
            }

            if (matched is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>("Rule input value is invalid.");

            Result allowed = ValidateAllowedValues(input, matched);
            if (allowed.IsFailure)
                return Result.Failure<IReadOnlyDictionary<string, RuleValue>>(allowed.Error);

            typed[key] = new RuleValueDto((ContractValueType)matched.Type, matched.Values);
        }

        return Validate(definitions, typed);
    }

    private static Result ValidateAllowedValues(RuleInputDefinition input, RuleValue value) =>
        input.AllowedValues.Count > 0 && value.Values.Any(candidate =>
            !input.AllowedValues.Contains(candidate, StringComparer.Ordinal))
            ? Result.Failure("Rule input value is not allowed.")
            : Result.Success();
}
