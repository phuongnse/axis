using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CancelWorkspaceDeletion;

public sealed class CancelWorkspaceDeletionHandler(
    IWorkspaceRepository workspaceRepo,
    IUnitOfWork uow)
    : ICommandHandler<CancelWorkspaceDeletionCommand>
{
    public async Task<Result> Handle(CancelWorkspaceDeletionCommand command, CancellationToken cancellationToken)
    {
        Workspace? Workspace = await workspaceRepo.GetByIdAsync(command.workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure(ErrorCodes.NotFound, "Workspace not found.");

        try
        {
            Workspace.CancelScheduledDeletion();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
