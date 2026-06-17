using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ScheduleWorkspaceDeletion;

public sealed class ScheduleWorkspaceDeletionHandler(
    IWorkspaceRepository workspaceRepo,
    IUserRepository userRepo,
    IEmailSender emailSender,
    IWorkspaceDeletionScheduler deletionScheduler,
    IUnitOfWork uow)
    : ICommandHandler<ScheduleWorkspaceDeletionCommand>
{
    public async Task<Result> Handle(ScheduleWorkspaceDeletionCommand command, CancellationToken cancellationToken)
    {
        Workspace? Workspace = await workspaceRepo.GetByIdAsync(command.workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure(ErrorCodes.NotFound, "Workspace not found.");

        if (!string.Equals(command.ConfirmationName, Workspace.Name, StringComparison.Ordinal))
            return Result.Failure(ErrorCodes.BusinessRule, "Confirmation name must match the Workspace name exactly.");

        User? requester = await userRepo.GetByIdAsync(command.RequestedByUserId, command.workspaceId, cancellationToken);
        if (requester is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        DateTime utcNow = DateTime.UtcNow;
        try
        {
            Workspace.ScheduleDeletion(utcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);

        if (Workspace.ScheduledHardDeleteAt is not DateTime hardDeleteAt)
            return Result.Failure(ErrorCodes.BusinessRule, "Deletion schedule was not created.");

        try
        {
            await deletionScheduler.ScheduleHardDeleteAsync(Workspace.Id, hardDeleteAt, cancellationToken);
        }
        catch (Exception)
        {
            try
            {
                Workspace.CancelScheduledDeletion();
                await uow.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Workspace state could not be rolled back — surface queue failure to caller.
            }

            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Failed to queue Workspace deletion. The Workspace was not scheduled for deletion.");
        }

        await emailSender.SendWorkspaceDeletionScheduledEmailAsync(
            requester.Email.Value,
            Workspace.Name,
            cancellationToken);

        return Result.Success();
    }
}
