using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;

public sealed class BeginWorkspaceContextTransitionHandler(
    IWorkspaceMembershipRepository memberships,
    IWorkspaceContextTransitionRepository transitions,
    IIdentityAuditOutbox audit,
    IUnitOfWork uow,
    TimeProvider clock,
    WorkspaceContextTransitionPolicy policy)
    : ICommandHandler<BeginWorkspaceContextTransitionCommand, WorkspaceContextTransitionDto>
{
    private const string AuditAction = "workspace.context.transition";
    private const string AuditTargetType = "WorkspaceContextTransition";

    public async Task<Result<WorkspaceContextTransitionDto>> Handle(
        BeginWorkspaceContextTransitionCommand command,
        CancellationToken ct)
    {
        if (command.UserId == Guid.Empty
            || command.SourceWorkspaceId == Guid.Empty
            || command.TargetWorkspaceId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return InvalidInput("Transition subject, Workspaces, and correlation are required.");
        }

        if (!await memberships.HasActiveWorkspaceAccessAsync(
                command.TargetWorkspaceId,
                command.UserId,
                ct))
        {
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.NotFound,
                "Workspace context is unavailable.");
        }

        WorkspaceContextTransition transition;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        try
        {
            WorkspaceContextTransitionPolicy validatedPolicy = policy.Validate();
            transition = WorkspaceContextTransition.Begin(
                command.UserId,
                command.SourceWorkspaceId,
                command.TargetWorkspaceId,
                command.SourceCorrelationDigest,
                command.TargetCorrelationDigest,
                now,
                now.Add(validatedPolicy.ConfirmationLifetime),
                now.Add(validatedPolicy.RetentionLifetime));
        }
        catch (ArgumentException ex)
        {
            return InvalidInput(ex.Message);
        }

        await transitions.AddAsync(transition, ct);
        await audit.EnqueueAsync(CreateRequestedAudit(transition, command.CorrelationId, now), ct);

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (UniqueConstraintException)
        {
            uow.ClearTracking();
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.Conflict,
                "A Workspace context transition already exists for this session.");
        }

        WorkspaceContextTransition? persisted = await transitions.GetByIdAsync(transition.Id, ct);
        IdentityAuditOutboxEntry? persistedAudit = await audit.GetAsync(transition.Id, ct);
        if (persisted is null || !IsExpectedRequestedAudit(persistedAudit, persisted))
        {
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.BusinessRule,
                "Workspace context transition could not be confirmed.");
        }

        return Result.Success(ToDto(persisted));
    }

    private static AuditEventV1 CreateRequestedAudit(
        WorkspaceContextTransition transition,
        string correlationId,
        DateTime occurredAt) =>
        new(
            transition.Id,
            AuditActorKindV1.Human,
            transition.UserId,
            transition.UserId,
            transition.TargetWorkspaceId,
            AuditAction,
            AuditTargetType,
            transition.Id,
            "requested",
            new DateTimeOffset(occurredAt),
            correlationId.Trim(),
            new Dictionary<string, string>
            {
                ["transitionId"] = transition.Id.ToString(),
            });

    private static bool IsExpectedRequestedAudit(
        IdentityAuditOutboxEntry? entry,
        WorkspaceContextTransition transition) =>
        entry is
        {
            State: IdentityAuditOutboxState.Pending or IdentityAuditOutboxState.Delivered,
            Event: var auditEvent,
        }
        && auditEvent.EventId == transition.Id
        && auditEvent.TargetId == transition.Id
        && auditEvent.WorkspaceId == transition.TargetWorkspaceId
        && StringComparer.Ordinal.Equals(auditEvent.Action, AuditAction)
        && StringComparer.Ordinal.Equals(auditEvent.TargetType, AuditTargetType)
        && StringComparer.Ordinal.Equals(auditEvent.Outcome, "requested");

    internal static WorkspaceContextTransitionDto ToDto(WorkspaceContextTransition transition) =>
        new(
            transition.Id,
            transition.SourceWorkspaceId,
            transition.TargetWorkspaceId,
            transition.Status.ToString(),
            transition.Revision,
            transition.ExpiresAt);

    private static Result<WorkspaceContextTransitionDto> InvalidInput(string detail) =>
        Result.Failure<WorkspaceContextTransitionDto>(ErrorCodes.InvalidInput, detail);
}
