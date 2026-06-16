using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ScheduleTeamAccountDeletion;

public sealed class ScheduleTeamAccountDeletionHandler(
    ITeamAccountRepository teamAccountRepo,
    IUserRepository userRepo,
    IEmailSender emailSender,
    ITeamAccountDeletionScheduler deletionScheduler,
    IUnitOfWork uow)
    : ICommandHandler<ScheduleTeamAccountDeletionCommand>
{
    public async Task<Result> Handle(ScheduleTeamAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(command.TeamAccountId, cancellationToken);
        if (teamAccount is null)
            return Result.Failure(ErrorCodes.NotFound, "Team account not found.");

        if (!string.Equals(command.ConfirmationName, teamAccount.Name, StringComparison.Ordinal))
            return Result.Failure(ErrorCodes.BusinessRule, "Confirmation name must match the team account name exactly.");

        User? requester = await userRepo.GetByIdAsync(command.RequestedByUserId, command.TeamAccountId, cancellationToken);
        if (requester is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found.");

        DateTime utcNow = DateTime.UtcNow;
        try
        {
            teamAccount.ScheduleDeletion(utcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);

        if (teamAccount.ScheduledHardDeleteAt is not DateTime hardDeleteAt)
            return Result.Failure(ErrorCodes.BusinessRule, "Deletion schedule was not created.");

        try
        {
            await deletionScheduler.ScheduleHardDeleteAsync(teamAccount.Id, hardDeleteAt, cancellationToken);
        }
        catch (Exception)
        {
            try
            {
                teamAccount.CancelScheduledDeletion();
                await uow.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // TeamAccount state could not be rolled back — surface queue failure to caller.
            }

            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Failed to queue team account deletion. The team account was not scheduled for deletion.");
        }

        await emailSender.SendTeamAccountDeletionScheduledEmailAsync(
            requester.Email.Value,
            teamAccount.Name,
            cancellationToken);

        return Result.Success();
    }
}
