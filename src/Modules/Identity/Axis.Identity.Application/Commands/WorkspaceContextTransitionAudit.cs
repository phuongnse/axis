using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Commands;

internal static class WorkspaceContextTransitionAudit
{
    private const string Action = "workspace.context.transition";
    private const string TargetType = "WorkspaceContextTransition";

    public static AuditEventV1 CreateTerminal(
        WorkspaceContextTransition transition,
        string outcome,
        string correlationId,
        DateTime occurredAt,
        AuditActorKindV1 actorKind = AuditActorKindV1.Human) =>
        new(
            transition.TerminalAuditEventId,
            actorKind,
            actorKind == AuditActorKindV1.System ? null : transition.UserId,
            transition.UserId,
            transition.TargetWorkspaceId,
            Action,
            TargetType,
            transition.Id,
            outcome,
            new DateTimeOffset(occurredAt),
            correlationId.Trim(),
            new Dictionary<string, string>
            {
                ["transitionId"] = transition.Id.ToString(),
            });

    public static bool IsExpectedTerminal(
        IdentityAuditOutboxEntry? entry,
        WorkspaceContextTransition transition) =>
        entry is
        {
            State: IdentityAuditOutboxState.Pending or IdentityAuditOutboxState.Delivered,
            Event: var auditEvent,
        }
        && auditEvent.EventId == transition.TerminalAuditEventId
        && auditEvent.TargetId == transition.Id
        && auditEvent.WorkspaceId == transition.TargetWorkspaceId
        && StringComparer.Ordinal.Equals(auditEvent.Action, Action)
        && StringComparer.Ordinal.Equals(auditEvent.TargetType, TargetType)
        && StringComparer.Ordinal.Equals(auditEvent.Outcome, Outcome(transition.Status));

    public static WorkspaceContextTransitionDto ToDto(WorkspaceContextTransition transition) =>
        new(
            transition.Id,
            transition.SourceWorkspaceId,
            transition.TargetWorkspaceId,
            transition.Status.ToString(),
            transition.Revision,
            transition.ExpiresAt);

    private static string Outcome(WorkspaceContextTransitionStatus status) => status switch
    {
        WorkspaceContextTransitionStatus.Completed => "completed",
        WorkspaceContextTransitionStatus.Compensated => "compensated",
        WorkspaceContextTransitionStatus.Failed => "failed",
        _ => throw new InvalidOperationException("Pending transitions do not have a terminal outcome."),
    };
}
