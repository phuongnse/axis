using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using DomainMappingKind = Axis.Rules.Domain.RuleInputMappingKind;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

internal static class RuleBindingValidator
{
    public static Result Validate(
        RuleDefinitionVersion version,
        IReadOnlyDictionary<string, RuleInputMapping> mappings)
    {
        HashSet<string> inputKeys = version.Inputs.Select(input => input.Key).ToHashSet(StringComparer.Ordinal);
        if (mappings.Keys.Any(key => !inputKeys.Contains(key)))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding mappings must match the rule input contract.");

        foreach (RuleInputDefinition input in version.Inputs)
        {
            if (!mappings.TryGetValue(input.Key, out RuleInputMapping? mapping))
            {
                if (input.IsRequired)
                    return Result.Failure(ErrorCodes.InvalidInput, $"Required rule input '{input.Key}' must be mapped.");
                continue;
            }
            if (mapping.Kind == DomainMappingKind.Context)
                continue;

            Result<RuleValue> value = CreateLiteral(input, mapping.LiteralValues);
            if (value.IsFailure)
                return Result.Failure(ErrorCodes.InvalidInput, value.Error);
        }

        return Result.Success();
    }

    public static Result ValidateRequestShape(CreateRuleBindingRequest request)
    {
        if (!Enum.IsDefined(request.FailureBehavior))
            return Result.Failure(ErrorCodes.InvalidInput, "Rule binding failure behavior is not supported.");
        return Result.Success();
    }

    private static Result<RuleValue> CreateLiteral(RuleInputDefinition input, IReadOnlyList<string> values)
    {
        foreach (DomainValueType type in input.Types)
        {
            Result<RuleValue> candidate = RuleValue.Create(
                type,
                values,
                input.AllowMultiple);
            if (candidate.IsSuccess &&
                (input.AllowedValues.Count == 0 || values.All(value => input.AllowedValues.Contains(value, StringComparer.Ordinal))))
                return candidate;
        }
        return Result.Failure<RuleValue>("Rule binding literal values do not match the rule input contract.");
    }
}
