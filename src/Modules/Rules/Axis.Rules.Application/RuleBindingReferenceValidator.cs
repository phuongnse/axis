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
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || bindingId == Guid.Empty)
            return RuleBindingReferenceValidationResult.Invalid("scope_required", "Workspace and binding are required.");
        RuleBinding? binding = await repository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(bindingId), workspaceId, cancellationToken);
        return binding is null
            ? RuleBindingReferenceValidationResult.Invalid("binding_not_found", "Rule binding was not found.")
            : binding.Enabled
                ? RuleBindingReferenceValidationResult.Valid(bindingId)
                : RuleBindingReferenceValidationResult.Invalid("binding_disabled", "Rule binding is disabled.");
    }
}
