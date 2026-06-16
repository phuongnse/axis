using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.PlanLimits;

namespace Axis.Identity.Infrastructure.PlanLimits;

internal sealed class UserPlanLimitUsageCounter(
    IUserRepository userRepo,
    IInvitationRepository invitationRepo) : IPlanLimitUsageCounter
{
    public PlanLimitResourceType ResourceType => PlanLimitResourceType.Users;

    public async Task<int> GetCurrentUsageAsync(Guid teamAccountId, CancellationToken cancellationToken = default)
    {
        int activeUsers = await userRepo.CountActiveUsersAsync(teamAccountId, cancellationToken);
        int pendingInvites = await invitationRepo.CountPendingAsync(teamAccountId, cancellationToken);
        return activeUsers + pendingInvites;
    }
}
