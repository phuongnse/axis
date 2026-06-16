using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.PlanLimits;

namespace Axis.Identity.Infrastructure.PlanLimits;

internal sealed class UserPlanLimitUsageCounter(
    IUserRepository userRepo,
    IInvitationRepository invitationRepo) : IPlanLimitUsageCounter
{
    public PlanLimitResourceType ResourceType => PlanLimitResourceType.Users;

    public async Task<int> GetCurrentUsageAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        int activeUsers = await userRepo.CountActiveUsersAsync(organizationId, cancellationToken);
        int pendingInvites = await invitationRepo.CountPendingAsync(organizationId, cancellationToken);
        return activeUsers + pendingInvites;
    }
}
