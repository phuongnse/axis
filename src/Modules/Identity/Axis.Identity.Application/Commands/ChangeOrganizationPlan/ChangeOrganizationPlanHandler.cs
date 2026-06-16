using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Subscriptions;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ChangeOrganizationPlan;

public sealed class ChangeOrganizationPlanHandler(
    IPlatformAdminAuthorization platformAdminAuthorization,
    IOrganizationRepository orgRepo,
    ISubscriptionPlanRepository planRepo,
    IOrganizationPlanChangeLogRepository changeLogRepo,
    IPlanLimitService planLimitService,
    IUnitOfWork uow)
    : ICommandHandler<ChangeOrganizationPlanCommand>
{
    public async Task<Result> Handle(ChangeOrganizationPlanCommand command, CancellationToken cancellationToken)
    {
        if (!platformAdminAuthorization.IsPlatformAdmin(command.ChangedByUserId))
            return Result.Failure(ErrorCodes.Forbidden, "Only platform administrators can change organization plans.");

        Domain.Aggregates.Organization? organization =
            await orgRepo.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.NotFound, "Organization not found.");

        SubscriptionPlan? newPlan = await planRepo.GetByIdAsync(command.NewPlanId, cancellationToken);
        if (newPlan is null || !newPlan.IsActive)
            return Result.Failure(ErrorCodes.NotFound, "Subscription plan not found.");

        Guid previousPlanId = organization.SubscriptionPlanId;
        if (previousPlanId == command.NewPlanId)
            return Result.Success();

        organization.ChangeSubscriptionPlan(command.NewPlanId);
        await changeLogRepo.AddAsync(
            command.OrganizationId,
            previousPlanId,
            command.NewPlanId,
            command.ChangedByUserId,
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RefreshCachedLimitsAsync(command.OrganizationId, cancellationToken);

        return Result.Success();
    }
}
