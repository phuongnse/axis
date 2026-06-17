using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ChangeWorkspacePlan;

public sealed class ChangeWorkspacePlanHandler(
    IPlatformAdminAuthorization platformAdminAuthorization,
    IWorkspaceRepository workspaceRepo,
    ISubscriptionPlanRepository planRepo,
    IWorkspacePlanChangeLogRepository changeLogRepo,
    IPlanLimitService planLimitService,
    IUnitOfWork uow)
    : ICommandHandler<ChangeWorkspacePlanCommand>
{
    public async Task<Result> Handle(ChangeWorkspacePlanCommand command, CancellationToken cancellationToken)
    {
        if (!platformAdminAuthorization.IsPlatformAdmin(command.ChangedByUserId))
            return Result.Failure(ErrorCodes.Forbidden, "Only platform administrators can change Workspace plans.");

        Domain.Aggregates.Workspace? Workspace =
            await workspaceRepo.GetByIdAsync(command.workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure(ErrorCodes.NotFound, "Workspace not found.");

        SubscriptionPlan? newPlan = await planRepo.GetByIdAsync(command.NewPlanId, cancellationToken);
        if (newPlan is null || !newPlan.IsActive)
            return Result.Failure(ErrorCodes.NotFound, "Subscription plan not found.");

        Guid previousPlanId = Workspace.SubscriptionPlanId;
        if (previousPlanId == command.NewPlanId)
            return Result.Success();

        Workspace.ChangeSubscriptionPlan(command.NewPlanId);
        await changeLogRepo.AddAsync(
            command.workspaceId,
            previousPlanId,
            command.NewPlanId,
            command.ChangedByUserId,
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RefreshCachedLimitsAsync(command.workspaceId, cancellationToken);

        return Result.Success();
    }
}
