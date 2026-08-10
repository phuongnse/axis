using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.HasWorkspaceInvitationHandoff;

public sealed class HasWorkspaceInvitationHandoffHandler(
    IWorkspaceInvitationRepository invitations,
    TimeProvider timeProvider)
    : IQueryHandler<HasWorkspaceInvitationHandoffQuery, bool>
{
    public async Task<bool> Handle(HasWorkspaceInvitationHandoffQuery query, CancellationToken ct)
    {
        WorkspaceInvitation? invitation = await invitations.GetByHandoffHashAsync(
            query.HandoffHash,
            ct);
        return invitation?.ClassifyHandoff(
            query.HandoffHash,
            timeProvider.GetUtcNow().UtcDateTime) == InvitationAcceptanceOutcome.Accepted;
    }
}
