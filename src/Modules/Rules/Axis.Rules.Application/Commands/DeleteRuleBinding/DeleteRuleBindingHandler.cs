using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Domain;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application.Commands.DeleteRuleBinding;

public sealed class DeleteRuleBindingHandler(
    ICurrentUser currentUser,
    ICurrentSubject currentSubject,
    IProductAuthorizationService authorization,
    IRuleBindingRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteRuleBindingCommand>
{
    public async Task<Result> Handle(DeleteRuleBindingCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return Result.Failure(ErrorCodes.Forbidden, "Current workspace scope is required.");
        if (command.BindingId == Guid.Empty)
            return Result.Failure(ErrorCodes.NotFound, "Rule binding was not found.");
        RuleBinding? binding = await repository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(command.BindingId), workspaceId, cancellationToken);
        if (binding is null)
            return Result.Failure(ErrorCodes.NotFound, "Rule binding was not found.");
        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(
                authorization, workspaceId, currentSubject.Subject,
                RuleProductActions.BindingManage, RuleProductActions.BindingResourceType,
                binding.DefinitionKey.Value, null, cancellationToken);
        if (!decision.IsAllowed)
            return RuleDefinitionFailures.Authorization(decision);
        if (binding.IsInstalled)
            return Result.Failure(ErrorCodes.Conflict, "Installed rule bindings are immutable.");
        if (command.ExpectedRevision != binding.Revision)
            return Result.Failure(ErrorCodes.Conflict, "The rule binding has changed.");
        repository.Remove(binding);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            return Result.Failure(ErrorCodes.Conflict, "The rule binding has changed.");
        }
        return Result.Success();
    }
}
