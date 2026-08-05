using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainLifecycleStatus = Axis.Rules.Domain.RuleLifecycleStatus;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

public sealed class RuleApplicationValidator(IRuleDefinitionRepository repository)
    : IRuleApplicationValidator
{
    public async Task<RuleApplicationValidationResult> ValidateAsync(
        RuleApplicationValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorkspaceId == Guid.Empty)
            return Invalid("workspace_required", "Workspace scope is required.");

        RuleDefinition? definition = BuiltInRuleCatalog.Find(request.DefinitionKey, request.DefinitionVersion);
        if (definition is null)
        {
            Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(request.DefinitionKey);
            if (key.IsFailure)
                return Invalid("definition_not_found", "Rule definition was not found.");

            definition = await repository.GetByKeyForWorkspaceAsync(
                key.Value,
                request.WorkspaceId,
                cancellationToken);
            if (definition is null)
                return Invalid("definition_not_found", "Rule definition was not found.");
        }

        RuleDefinitionVersion? version = definition.FindVersion(request.DefinitionVersion);
        return version is null
            ? Invalid("version_not_found", "Published rule version was not found.")
            : ValidateInputs(version.Inputs, request.Inputs, request.InputTypes);
    }

    private static RuleApplicationValidationResult ValidateInputs(
        IReadOnlyList<RuleInputDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> inputs,
        IReadOnlyDictionary<string, ContractValueType>? inputTypes)
    {
        IReadOnlyDictionary<string, DomainValueType>? preferredTypes = inputTypes?.ToDictionary(
            pair => pair.Key,
            pair => (DomainValueType)pair.Value,
            StringComparer.Ordinal);
        Result<IReadOnlyDictionary<string, RuleValue>> result = RuleInputValidator.ValidateRaw(
            definitions,
            inputs,
            preferredTypes);
        if (result.IsSuccess && inputTypes is not null)
        {
            foreach ((string key, ContractValueType expectedType) in inputTypes)
            {
                if (!result.Value.TryGetValue(key, out RuleValue? value) ||
                    value.Type != (DomainValueType)expectedType)
                {
                    return Invalid("input_incompatible", "Rule input is incompatible with the consumer binding.");
                }
            }
        }
        return result.IsSuccess
            ? RuleApplicationValidationResult.Valid(result.Value.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Values.ToArray(),
                StringComparer.Ordinal))
            : Invalid("input_invalid", result.Error);
    }

    private static RuleApplicationValidationResult Invalid(string errorCode, string error) =>
        RuleApplicationValidationResult.Invalid(errorCode, error);
}
