using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.SetWorkspaceProductBuilder;

public sealed class SetWorkspaceProductBuilderHandler(
    IWorkspaceRepository workspaces,
    IWorkspaceMembershipRepository memberships,
    IIdentityAuditOutbox auditOutbox,
    TimeProvider timeProvider,
    IUnitOfWork uow)
    : ICommandHandler<SetWorkspaceProductBuilderCommand, WorkspaceProductBuilderDto>
{
    public async Task<Result<WorkspaceProductBuilderDto>> Handle(
        SetWorkspaceProductBuilderCommand command,
        CancellationToken ct)
    {
        if (command.ActorUserId == Guid.Empty
            || command.WorkspaceId == Guid.Empty
            || command.TargetUserId == Guid.Empty
            || command.ExpectedRevision <= 0
            || string.IsNullOrWhiteSpace(command.CorrelationId)
            || string.IsNullOrWhiteSpace(command.ActorDisplayName))
            return Invalid();

        Workspace? workspace = await workspaces.GetByIdAsync(command.WorkspaceId, ct);
        if (workspace is not { Status: WorkspaceStatus.Active })
            return NotFound();
        if (workspace.Type != WorkspaceType.Organization)
            return await PersistDenied(command, "workspace_ineligible", NotFound(), ct);

        WorkspaceMembership? actor = await memberships.GetActiveHumanAsync(
            command.WorkspaceId,
            command.ActorUserId,
            ct);
        if (actor is not
            {
                Role: WorkspaceMembershipRole.Administrator,
                Status: MembershipStatus.Active,
            }
            || command.ActorUserId == command.TargetUserId)
            return await PersistDenied(command, "authority_denied", Forbidden(), ct);

        WorkspaceMembership? target = await memberships.GetActiveHumanAsync(
            command.WorkspaceId,
            command.TargetUserId,
            ct);
        if (target is null)
            return await PersistDenied(command, "target_unavailable", NotFound(), ct);
        if (target.Revision != command.ExpectedRevision)
            return await PersistDenied(command, "revision_conflict", Conflict(), ct);

        try
        {
            bool stateChanged = target.IsProductBuilder != command.Enabled;
            target.SetProductBuilder(command.Enabled, command.ExpectedRevision);
            if (stateChanged)
            {
                target.RecordModification(
                    ActorSnapshot.User(command.ActorUserId, command.ActorDisplayName),
                    timeProvider.GetUtcNow().UtcDateTime);
            }
        }
        catch (InvalidOperationException)
        {
            return Conflict();
        }

        Guid auditEventId = Guid.NewGuid();
        AuditEventV1 expectedAudit = CreateAudit(auditEventId, command, target);
        try
        {
            await auditOutbox.EnqueueAsync(expectedAudit, ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
            return await PersistDenied(command, "concurrent_change", Conflict(), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return AuditUnavailable();
        }

        try
        {
            WorkspaceMembership? persisted = await memberships.GetActiveHumanAsync(
                command.WorkspaceId,
                command.TargetUserId,
                ct);
            IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(auditEventId, ct);
            if (persisted is null
                || persisted.IsProductBuilder != command.Enabled
                || persisted.Revision != target.Revision
                || !IsExpectedAudit(audit, expectedAudit))
                return ReadBackFailed();

            IReadOnlyList<ActiveWorkspaceHumanProjection> projections =
                await memberships.ListActiveForWorkspaceAsync(command.WorkspaceId, ct);
            ActiveWorkspaceHumanProjection? projection = projections.SingleOrDefault(
                member => member.UserId == command.TargetUserId);
            return projection is null
                ? ReadBackFailed()
                : Result.Success(new WorkspaceProductBuilderDto(
                    projection.UserId,
                    projection.DisplayName,
                    projection.Email,
                    projection.WorkspaceRole.ToString(),
                    projection.IsProductBuilder,
                    projection.MembershipRevision,
                    CanChange: true,
                    projection.Metadata));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ReadBackFailed();
        }
    }

    private async Task<Result<WorkspaceProductBuilderDto>> PersistDenied(
        SetWorkspaceProductBuilderCommand command,
        string outcome,
        Result<WorkspaceProductBuilderDto> deniedResult,
        CancellationToken ct)
    {
        Guid eventId = Guid.NewGuid();
        AuditEventV1 expectedAudit = new(
            eventId,
            AuditActorKindV1.Human,
            command.ActorUserId,
            command.ActorUserId,
            command.WorkspaceId,
            command.Enabled
                ? "workspace.product_builder.grant_denied"
                : "workspace.product_builder.revoke_denied",
            "WorkspaceProductBuilderAttempt",
            eventId,
            outcome,
            timeProvider.GetUtcNow(),
            command.CorrelationId.Trim(),
            new Dictionary<string, string>
            {
                ["workspaceId"] = command.WorkspaceId.ToString(),
                ["enabled"] = command.Enabled.ToString(),
            });
        try
        {
            await auditOutbox.EnqueueAsync(expectedAudit, ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
            IdentityAuditOutboxEntry? persisted = await auditOutbox.GetAsync(eventId, ct);
            return IsExpectedAudit(persisted, expectedAudit)
                ? deniedResult
                : AuditUnavailable();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return AuditUnavailable();
        }
    }

    private AuditEventV1 CreateAudit(
        Guid eventId,
        SetWorkspaceProductBuilderCommand command,
        WorkspaceMembership target) =>
        new(
            eventId,
            AuditActorKindV1.Human,
            command.ActorUserId,
            command.TargetUserId,
            command.WorkspaceId,
            command.Enabled
                ? "workspace.product_builder.granted"
                : "workspace.product_builder.revoked",
            "WorkspaceMembership",
            target.Id,
            "succeeded",
            timeProvider.GetUtcNow(),
            command.CorrelationId.Trim(),
            new Dictionary<string, string>
            {
                ["workspaceId"] = command.WorkspaceId.ToString(),
                ["enabled"] = command.Enabled.ToString(),
                ["membershipRevision"] = target.Revision.ToString(),
            });

    private static bool IsExpectedAudit(
        IdentityAuditOutboxEntry? persisted,
        AuditEventV1 expected) =>
        persisted is not null
        && persisted.State is IdentityAuditOutboxState.Pending or IdentityAuditOutboxState.Delivered
        && persisted.Event.EventId == expected.EventId
        && persisted.Event.ActorKind == expected.ActorKind
        && persisted.Event.ActorId == expected.ActorId
        && persisted.Event.SubjectId == expected.SubjectId
        && persisted.Event.WorkspaceId == expected.WorkspaceId
        && StringComparer.Ordinal.Equals(persisted.Event.Action, expected.Action)
        && StringComparer.Ordinal.Equals(persisted.Event.TargetType, expected.TargetType)
        && persisted.Event.TargetId == expected.TargetId
        && StringComparer.Ordinal.Equals(persisted.Event.Outcome, expected.Outcome)
        && StringComparer.Ordinal.Equals(persisted.Event.CorrelationId, expected.CorrelationId)
        && expected.Metadata is not null
        && persisted.Event.Metadata is not null
        && expected.Metadata.Count == persisted.Event.Metadata.Count
        && expected.Metadata.All(pair =>
            persisted.Event.Metadata.TryGetValue(pair.Key, out string? value)
            && StringComparer.Ordinal.Equals(pair.Value, value));

    private static Result<WorkspaceProductBuilderDto> Invalid() => Failure(
        ErrorCodes.InvalidInput,
        "Actor, Workspace, target, revision, and correlation are required.",
        IdentityProblemCodes.ProductBuilderInvalid);

    private static Result<WorkspaceProductBuilderDto> Forbidden() => Failure(
        ErrorCodes.Forbidden,
        "Product Builder management authority is required for a different member.",
        IdentityProblemCodes.ProductBuilderForbidden);

    private static Result<WorkspaceProductBuilderDto> NotFound() => Failure(
        ErrorCodes.NotFound,
        "Workspace member was not found.",
        IdentityProblemCodes.ProductBuilderNotFound);

    private static Result<WorkspaceProductBuilderDto> Conflict() => Failure(
        ErrorCodes.Conflict,
        "Workspace membership revision is stale.",
        IdentityProblemCodes.ProductBuilderConflict);

    private static Result<WorkspaceProductBuilderDto> AuditUnavailable() => Failure(
        ErrorCodes.Unavailable,
        "The Product Builder outcome could not be audited.",
        IdentityProblemCodes.ProductBuilderAuditUnavailable);

    private static Result<WorkspaceProductBuilderDto> ReadBackFailed() => Failure(
        ErrorCodes.Unavailable,
        "The Product Builder outcome could not be confirmed.",
        IdentityProblemCodes.ProductBuilderReadBackFailed);

    private static Result<WorkspaceProductBuilderDto> Failure(
        string errorCode,
        string detail,
        string problemCode) =>
        Result.Failure<WorkspaceProductBuilderDto>(errorCode, detail, problemCode);
}
