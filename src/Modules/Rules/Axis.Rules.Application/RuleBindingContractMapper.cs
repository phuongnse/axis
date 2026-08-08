using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractFailureBehavior = Axis.Rules.Contracts.RuleBindingFailureBehavior;
using ContractMappingKind = Axis.Rules.Contracts.RuleInputMappingKind;
using DomainFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;
using DomainMappingKind = Axis.Rules.Domain.RuleInputMappingKind;

namespace Axis.Rules.Application;

internal static class RuleBindingContractMapper
{
    public static RuleBindingDto ToDto(RuleBinding binding) =>
        new(
            binding.Id.Value,
            binding.WorkspaceId,
            binding.DefinitionKey.Value,
            binding.DefinitionVersion,
            binding.TargetType,
            binding.TargetId,
            binding.UseCaseOrTrigger,
            binding.InputMappings.ToDictionary(pair => pair.Key, pair => ToDto(pair.Value), StringComparer.Ordinal),
            binding.Priority,
            binding.Enabled,
            (ContractFailureBehavior)binding.FailureBehavior,
            binding.Revision,
            binding.CreatedAt,
            binding.UpdatedAt);

    public static RuleBindingUsageDto ToUsageDto(RuleBinding binding) =>
        new(
            binding.Id.Value,
            binding.DefinitionKey.Value,
            binding.DefinitionVersion,
            binding.TargetType,
            binding.TargetId,
            binding.UseCaseOrTrigger,
            binding.Priority,
            binding.Enabled,
            (ContractFailureBehavior)binding.FailureBehavior,
            binding.Revision);

    public static RuleBindingSolutionComponent ToSolutionComponent(RuleBinding binding) =>
        new(
            binding.InstalledComponentKey!,
            binding.DefinitionKey.Value,
            binding.DefinitionVersion,
            binding.TargetType,
            binding.TargetId,
            binding.UseCaseOrTrigger,
            binding.InputMappings.ToDictionary(
                pair => pair.Key,
                pair => ToDto(pair.Value),
                StringComparer.Ordinal),
            binding.Priority,
            binding.Enabled,
            (ContractFailureBehavior)binding.FailureBehavior);

    public static Result<IReadOnlyDictionary<string, RuleInputMapping>> ToDomain(
        IReadOnlyDictionary<string, RuleInputMappingDto>? mappings)
    {
        if (mappings is null)
            return Result.Failure<IReadOnlyDictionary<string, RuleInputMapping>>("Rule binding input mappings are required.");

        Dictionary<string, RuleInputMapping> result = new(StringComparer.Ordinal);
        foreach ((string key, RuleInputMappingDto mapping) in mappings)
        {
            if (string.IsNullOrWhiteSpace(key) || mapping is null)
                return Result.Failure<IReadOnlyDictionary<string, RuleInputMapping>>("Rule binding input mappings must contain a key and value.");
            Result<RuleInputMapping> mapped = mapping.Kind switch
            {
                ContractMappingKind.Context => RuleInputMapping.FromContext(mapping.ContextKey ?? string.Empty),
                ContractMappingKind.Literal => RuleInputMapping.FromLiteral(mapping.LiteralValues),
                _ => Result.Failure<RuleInputMapping>("Rule input mapping kind is not supported."),
            };
            if (mapped.IsFailure)
                return Result.Failure<IReadOnlyDictionary<string, RuleInputMapping>>(mapped.Error);
            if (!result.TryAdd(key, mapped.Value))
                return Result.Failure<IReadOnlyDictionary<string, RuleInputMapping>>("Rule input mappings must be unique.");
        }
        return result;
    }

    private static RuleInputMappingDto ToDto(RuleInputMapping mapping) =>
        new(
            (ContractMappingKind)mapping.Kind,
            mapping.ContextKey,
            mapping.LiteralValues);
}
