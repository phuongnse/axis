using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ChangeTeamAccountPlan;

public sealed class ChangeTeamAccountPlanHandler(
    IPlatformAdminAuthorization platformAdminAuthorization,
    ITeamAccountRepository teamAccountRepo,
    ISubscriptionPlanRepository planRepo,
    ITeamAccountPlanChangeLogRepository changeLogRepo,
    IPlanLimitService planLimitService,
    IUnitOfWork uow)
    : ICommandHandler<ChangeTeamAccountPlanCommand>
{
    public async Task<Result> Handle(ChangeTeamAccountPlanCommand command, CancellationToken cancellationToken)
    {
        if (!platformAdminAuthorization.IsPlatformAdmin(command.ChangedByUserId))
            return Result.Failure(ErrorCodes.Forbidden, "Only platform administrators can change team account plans.");

        Domain.Aggregates.TeamAccount? teamAccount =
            await teamAccountRepo.GetByIdAsync(command.TeamAccountId, cancellationToken);
        if (teamAccount is null)
            return Result.Failure(ErrorCodes.NotFound, "Team account not found.");

        SubscriptionPlan? newPlan = await planRepo.GetByIdAsync(command.NewPlanId, cancellationToken);
        if (newPlan is null || !newPlan.IsActive)
            return Result.Failure(ErrorCodes.NotFound, "Subscription plan not found.");

        Guid previousPlanId = teamAccount.SubscriptionPlanId;
        if (previousPlanId == command.NewPlanId)
            return Result.Success();

        teamAccount.ChangeSubscriptionPlan(command.NewPlanId);
        await changeLogRepo.AddAsync(
            command.TeamAccountId,
            previousPlanId,
            command.NewPlanId,
            command.ChangedByUserId,
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RefreshCachedLimitsAsync(command.TeamAccountId, cancellationToken);

        return Result.Success();
    }
}
