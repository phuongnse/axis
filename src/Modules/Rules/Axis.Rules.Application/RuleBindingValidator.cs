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

    public static Result ValidateConsumerContext(
        RuleDefinitionVersion version,
        IReadOnlyDictionary<string, RuleInputMapping> mappings,
        IReadOnlyDictionary<string, RuleBindingContextValueSchema> contextValues,
        IReadOnlyList<string> requiredContextKeys)
    {
        Result mappingsValid = Validate(version, mappings);
        if (mappingsValid.IsFailure)
            return mappingsValid;

        foreach ((string inputKey, RuleInputMapping mapping) in mappings)
        {
            if (mapping.Kind != DomainMappingKind.Context)
                continue;

            if (!contextValues.TryGetValue(mapping.ContextKey!, out RuleBindingContextValueSchema? context))
                return Result.Failure("binding_context_key_not_found", "Rule binding references an unavailable consumer context key.");

            RuleInputDefinition input = version.Inputs.Single(input => input.Key == inputKey);
            if (!input.Types.Contains((DomainValueType)context.Type))
                return Result.Failure("binding_context_type_mismatch", "Rule binding consumer context type does not match the rule input.");
            if (context.AllowMultiple && !input.AllowMultiple)
                return Result.Failure("binding_context_cardinality_mismatch", "Multiple consumer values cannot be mapped to a scalar rule input.");
        }

        foreach (string requiredContextKey in requiredContextKeys)
        {
            if (!mappings.Values.Any(mapping =>
                    mapping.Kind == DomainMappingKind.Context &&
                    StringComparer.Ordinal.Equals(mapping.ContextKey, requiredContextKey)))
                return Result.Failure("binding_required_context_unmapped", "A required consumer context key is not mapped by the rule binding.");
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
