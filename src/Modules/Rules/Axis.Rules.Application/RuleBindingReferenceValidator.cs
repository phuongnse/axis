using Axis.Rules.Application.Repositories;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;

namespace Axis.Rules.Application;

public sealed class RuleBindingReferenceValidator(IRuleBindingRepository repository)
    : IRuleBindingReferenceValidator
{
    public async Task<RuleBindingReferenceValidationResult> ValidateAsync(
        Guid workspaceId,
        Guid bindingId,
        CancellationToken cancellationToken = default,
        string? expectedTargetType = null,
        string? expectedTargetId = null,
        string? expectedUseCaseOrTrigger = null)
    {
        if (workspaceId == Guid.Empty || bindingId == Guid.Empty)
            return RuleBindingReferenceValidationResult.Invalid("scope_required", "Workspace and binding are required.");
        RuleBinding? binding = await repository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(bindingId), workspaceId, cancellationToken);
        if (binding is null)
            return RuleBindingReferenceValidationResult.Invalid("binding_not_found", "Rule binding was not found.");
        if (!binding.Enabled)
            return RuleBindingReferenceValidationResult.Invalid("binding_disabled", "Rule binding is disabled.");
        if (expectedTargetType is not null &&
            !StringComparer.Ordinal.Equals(binding.TargetType, expectedTargetType))
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_target_mismatch",
                "Rule binding target type does not match the consuming field.");
        if (expectedTargetId is not null &&
            !StringComparer.Ordinal.Equals(binding.TargetId, expectedTargetId))
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_target_mismatch",
                "Rule binding target ID does not match the consuming field.");
        if (expectedUseCaseOrTrigger is not null &&
            !StringComparer.Ordinal.Equals(binding.UseCaseOrTrigger, expectedUseCaseOrTrigger))
            return RuleBindingReferenceValidationResult.Invalid(
                "binding_trigger_mismatch",
                "Rule binding trigger does not match the consuming workflow.");

        return RuleBindingReferenceValidationResult.Valid(bindingId, binding.Revision);
    }
}
