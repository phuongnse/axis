using Axis.Audit.Contracts;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Commands;

internal static class WorkspaceInvitationAudit
{
    public static AuditEventV1 Create(
        Guid eventId,
        AuditActorKindV1 actorKind,
        Guid? actorId,
        Guid? subjectId,
        WorkspaceInvitation invitation,
        string action,
        string outcome,
        string correlationId,
        DateTime occurredAt) =>
        new(
            eventId,
            actorKind,
            actorId,
            subjectId,
            invitation.WorkspaceId,
            action,
            "WorkspaceInvitation",
            invitation.Id,
            outcome,
            new DateTimeOffset(occurredAt),
            correlationId.Trim(),
            new Dictionary<string, string>
            {
                ["organizationId"] = invitation.OrganizationId.ToString(),
                ["workspaceId"] = invitation.WorkspaceId.ToString(),
                ["requestedRole"] = invitation.RequestedRole.ToString(),
                ["generation"] = invitation.CurrentToken.Generation.ToString(),
            });
}
