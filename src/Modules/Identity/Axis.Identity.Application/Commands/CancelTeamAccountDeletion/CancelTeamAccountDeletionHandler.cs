using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CancelTeamAccountDeletion;

public sealed class CancelTeamAccountDeletionHandler(
    ITeamAccountRepository teamAccountRepo,
    IUnitOfWork uow)
    : ICommandHandler<CancelTeamAccountDeletionCommand>
{
    public async Task<Result> Handle(CancelTeamAccountDeletionCommand command, CancellationToken cancellationToken)
    {
        TeamAccount? teamAccount = await teamAccountRepo.GetByIdAsync(command.TeamAccountId, cancellationToken);
        if (teamAccount is null)
            return Result.Failure(ErrorCodes.NotFound, "Team account not found.");

        try
        {
            teamAccount.CancelScheduledDeletion();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
