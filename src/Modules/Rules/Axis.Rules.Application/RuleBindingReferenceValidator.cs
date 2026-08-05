using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application;

public sealed class RuleBindingReferenceValidator(
    IRuleBindingRepository bindingRepository,
    IRuleDefinitionRepository definitionRepository)
    : IRuleBindingReferenceValidator
{
    public async Task<RuleBindingReferenceValidationResult> ValidateAsync(
        RuleBindingReferenceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorkspaceId == Guid.Empty || request.BindingId == Guid.Empty)
            return RuleBindingReferenceValidationResult.Invalid("scope_required", "Workspace and binding are required.");
        RuleBinding? binding = await bindingRepository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(request.BindingId), request.WorkspaceId, cancellationToken);
        if (binding is null)
            return RuleBindingReferenceValidationResult.Invalid("binding_not_found", "Rule binding was not found.");

        if (request.ExpectedBindingRevision is int expectedRevision && binding.Revision != expectedRevision)
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_revision_conflict",
                "Rule binding has changed.");

        if (!binding.Enabled)
            return RuleBindingReferenceValidationResult.Invalid("binding_disabled", "Rule binding is disabled.");
        if (!StringComparer.Ordinal.Equals(binding.TargetType, request.ExpectedTargetType))
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_target_mismatch",
                "Rule binding target type does not match the consuming field.");
        if (!StringComparer.Ordinal.Equals(binding.TargetId, request.ExpectedTargetId))
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_target_mismatch",
                "Rule binding target ID does not match the consuming field.");
        if (!StringComparer.Ordinal.Equals(binding.UseCaseOrTrigger, request.ExpectedUseCaseOrTrigger))
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_trigger_mismatch",
                "Rule binding trigger does not match the consuming workflow.");

        Result<RuleDefinitionVersion> definition = await ResolveExactVersionAsync(
            request.WorkspaceId,
            binding,
            cancellationToken);
        if (definition.IsFailure)
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_definition_unavailable",
                "Rule binding definition version is unavailable.");

        Result context = RuleBindingValidator.ValidateConsumerContext(
            definition.Value,
            binding.InputMappings,
            request.ContextValues,
            request.RequiredContextKeys);
        if (context.IsFailure)
            return RuleBindingReferenceValidationResult.Invalid(context.ErrorCode!, context.Error);

        return RuleBindingReferenceValidationResult.Valid(request.BindingId, binding.Revision);
    }

    private async Task<Result<RuleDefinitionVersion>> ResolveExactVersionAsync(
        Guid workspaceId,
        RuleBinding binding,
        CancellationToken cancellationToken)
    {
        RuleDefinition? builtIn = BuiltInRuleCatalog.Find(binding.DefinitionKey.Value, binding.DefinitionVersion);
        if (builtIn is not null)
            return builtIn.FindVersion(binding.DefinitionVersion)!;

        RuleDefinition? definition = await definitionRepository.GetByKeyForWorkspaceAsync(
            binding.DefinitionKey,
            workspaceId,
            cancellationToken);
        if (definition is null || definition.ArchivedAt is not null)
            return Result.Failure<RuleDefinitionVersion>("binding_definition_unavailable", "Rule definition was not found.");

        RuleDefinitionVersion? version = definition.FindVersion(binding.DefinitionVersion);
        return version is null
            ? Result.Failure<RuleDefinitionVersion>("binding_definition_unavailable", "Rule definition version was not found.")
            : version;
    }
}
