using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application;

public sealed record WorkspaceInvitationLifecycleDto(
    Guid InvitationId,
    string? RecipientEmail,
    string RequestedRole,
    string Status,
    string DeliveryStatus,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    int Revision);

public sealed record WorkspaceInvitationExchangeDto(string Outcome, string? HandoffSecret);

public sealed record WorkspaceInvitationReviewDto(
    Guid InvitationId,
    Guid WorkspaceId,
    string OrganizationName,
    string WorkspaceName,
    string InviterName,
    string RequestedRole,
    DateTime ExpiresAt);

public sealed record WorkspaceInvitationAcceptanceDto(
    string Outcome,
    Guid? WorkspaceId,
    string? OrganizationRole,
    string? WorkspaceRole);

internal static class WorkspaceInvitationDtoMapping
{
    public static WorkspaceInvitationLifecycleDto ToLifecycleDto(this WorkspaceInvitation invitation) =>
        new(
            invitation.Id,
            invitation.NormalizedEmail,
            invitation.RequestedRole.ToString(),
            invitation.Status.ToString(),
            invitation.CurrentToken.DeliveryStatus.ToString(),
            invitation.CreatedAt,
            invitation.ExpiresAt,
            invitation.Revision);
}
