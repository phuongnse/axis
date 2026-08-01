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
    IRuleBindingRepository repository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteRuleBindingCommand>
{
    public async Task<Result> Handle(DeleteRuleBindingCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return Result.Failure(ErrorCodes.Forbidden, "Current workspace scope is required.");
        RuleBinding? binding = await repository.GetByIdForWorkspaceAsync(
            RuleBindingId.From(command.BindingId), workspaceId, cancellationToken);
        if (binding is null)
            return Result.Failure(ErrorCodes.NotFound, "Rule binding was not found.");
        repository.Remove(binding);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
