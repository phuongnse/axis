using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

internal static class WorkspaceContextTransitionHandlerTestData
{
    public const string SourceDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string TargetDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static WorkspaceContextTransition Pending(DateTime? now = null)
    {
        DateTime createdAt = now ?? DateTime.UtcNow;
        return WorkspaceContextTransition.Begin(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SourceDigest,
            TargetDigest,
            createdAt,
            createdAt.AddMinutes(5),
            createdAt.AddHours(1));
    }

    public static void ConfigureReadBack(
        IWorkspaceContextTransitionRepository transitions,
        IIdentityAuditOutbox audit,
        WorkspaceContextTransition transition)
    {
        transitions.GetByIdAsync(transition.Id, Arg.Any<CancellationToken>())
            .Returns(transition);
        audit.GetAsync(transition.TerminalAuditEventId, Arg.Any<CancellationToken>())
            .Returns(_ => TerminalAudit(transition));
    }

    private static IdentityAuditOutboxEntry TerminalAudit(WorkspaceContextTransition transition)
    {
        string outcome = transition.Status switch
        {
            WorkspaceContextTransitionStatus.Completed => "completed",
            WorkspaceContextTransitionStatus.Compensated => "compensated",
            WorkspaceContextTransitionStatus.Failed => "failed",
            _ => "pending",
        };
        AuditEventV1 auditEvent = new(
            transition.TerminalAuditEventId,
            AuditActorKindV1.Human,
            transition.UserId,
            transition.UserId,
            transition.TargetWorkspaceId,
            "workspace.context.transition",
            "WorkspaceContextTransition",
            transition.Id,
            outcome,
            new DateTimeOffset(transition.TerminalAt ?? transition.CreatedAt),
            "test-correlation",
            new Dictionary<string, string>
            {
                ["transitionId"] = transition.Id.ToString(),
            });
        return new IdentityAuditOutboxEntry(auditEvent, IdentityAuditOutboxState.Pending);
    }
}
