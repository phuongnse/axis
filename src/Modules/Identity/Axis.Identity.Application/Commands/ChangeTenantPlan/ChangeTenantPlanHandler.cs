using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ChangeTenantPlan;

public sealed class ChangeTenantPlanHandler(
    IPlatformAdminAuthorization platformAdminAuthorization,
    ITenantRepository tenantRepo,
    ISubscriptionPlanRepository planRepo,
    ITenantPlanChangeLogRepository changeLogRepo,
    IPlanLimitService planLimitService,
    IUnitOfWork uow)
    : ICommandHandler<ChangeTenantPlanCommand>
{
    public async Task<Result> Handle(ChangeTenantPlanCommand command, CancellationToken cancellationToken)
    {
        if (!platformAdminAuthorization.IsPlatformAdmin(command.ChangedByUserId))
            return Result.Failure(ErrorCodes.Forbidden, "Only platform administrators can change Tenant plans.");

        Domain.Aggregates.Tenant? Tenant =
            await tenantRepo.GetByIdAsync(command.tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure(ErrorCodes.NotFound, "Tenant not found.");

        SubscriptionPlan? newPlan = await planRepo.GetByIdAsync(command.NewPlanId, cancellationToken);
        if (newPlan is null || !newPlan.IsActive)
            return Result.Failure(ErrorCodes.NotFound, "Subscription plan not found.");

        Guid previousPlanId = Tenant.SubscriptionPlanId;
        if (previousPlanId == command.NewPlanId)
            return Result.Success();

        Tenant.ChangeSubscriptionPlan(command.NewPlanId);
        await changeLogRepo.AddAsync(
            command.tenantId,
            previousPlanId,
            command.NewPlanId,
            command.ChangedByUserId,
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RefreshCachedLimitsAsync(command.tenantId, cancellationToken);

        return Result.Success();
    }
}
