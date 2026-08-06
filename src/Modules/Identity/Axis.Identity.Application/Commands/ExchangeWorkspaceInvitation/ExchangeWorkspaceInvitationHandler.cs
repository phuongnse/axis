using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ExchangeWorkspaceInvitation;

public sealed class ExchangeWorkspaceInvitationHandler(
    IWorkspaceInvitationRepository invitations,
    IWorkspaceInvitationRateLimiter rateLimiter,
    IIdentityAuditOutbox auditOutbox,
    WorkspaceInvitationPolicy policy,
    TimeProvider timeProvider,
    IUnitOfWork uow)
    : ICommandHandler<ExchangeWorkspaceInvitationCommand, WorkspaceInvitationExchangeDto>
{
    public async Task<Result<WorkspaceInvitationExchangeDto>> Handle(
        ExchangeWorkspaceInvitationCommand command,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RequestPartition)
            || string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return Invalid();
        }

        bool hasOpaqueToken = IsOpaqueToken(command.RawToken);
        string tokenHash = hasOpaqueToken
            ? OpaqueTokenGenerator.Hash(command.RawToken)
            : "invalid-token-shape";
        Result limit = await rateLimiter.AcquireExchangeAsync(
            command.RequestPartition,
            tokenHash,
            ct);
        if (limit.IsFailure)
        {
            if (!await PersistPlatformRejection(command.CorrelationId, "rate_limited", ct))
                return AuditUnavailable();

            return Result.Failure<WorkspaceInvitationExchangeDto>(
                ErrorCodes.RateLimited,
                "Invitation access is temporarily unavailable.",
                limit.ProblemCode ?? IdentityProblemCodes.InvitationRateLimited);
        }

        if (!hasOpaqueToken)
        {
            if (!await PersistPlatformRejection(command.CorrelationId, "invalid", ct))
                return AuditUnavailable();

            return Invalid();
        }

        WorkspaceInvitation? invitation = await invitations.GetByTokenHashAsync(tokenHash, ct);
        if (invitation is null)
        {
            if (!await PersistPlatformRejection(command.CorrelationId, "invalid", ct))
                return AuditUnavailable();

            return Invalid();
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        (string rawHandoff, string handoffHash) = OpaqueTokenGenerator.Create();
        InvitationExchangeOutcome outcome = invitation.Exchange(
            tokenHash,
            handoffHash,
            now.Add(policy.HandoffLifetime),
            now,
            invitation.Revision);

        Guid auditEventId = Guid.NewGuid();
        await auditOutbox.EnqueueAsync(
            WorkspaceInvitationAudit.Create(
                auditEventId,
                AuditActorKindV1.Anonymous,
                null,
                null,
                invitation,
                "workspace.invitation.exchanged",
                outcome.ToString().ToLowerInvariant(),
                command.CorrelationId,
                now),
            ct);

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
            return Invalid();
        }

        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(auditEventId, ct);
        if (audit is null || audit.State == IdentityAuditOutboxState.Poisoned)
            return ReadBackFailure();

        if (outcome != InvitationExchangeOutcome.Exchanged)
            return Invalid();

        WorkspaceInvitation? persisted = await invitations.GetByHandoffHashAsync(handoffHash, ct);
        return persisted?.ClassifyHandoff(handoffHash, now) == InvitationAcceptanceOutcome.Accepted
            ? Result.Success(new WorkspaceInvitationExchangeDto("Exchanged", rawHandoff))
            : ReadBackFailure();
    }

    private static bool IsOpaqueToken(string token) =>
        token.Length == 64 && token.All(Uri.IsHexDigit);

    private async Task<bool> PersistPlatformRejection(
        string correlationId,
        string outcome,
        CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset occurredAt = now.AddTicks(-(now.Ticks % 10));
        AuditEventV1 auditEvent = new(
            Guid.NewGuid(),
            AuditActorKindV1.Anonymous,
            null,
            null,
            null,
            "workspace.invitation.exchange_rejected",
            "WorkspaceInvitationAccessAttempt",
            Guid.NewGuid(),
            outcome,
            occurredAt,
            correlationId.Trim());

        try
        {
            await auditOutbox.EnqueueAsync(auditEvent, ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();

            IdentityAuditOutboxEntry? persisted = await auditOutbox.GetAsync(auditEvent.EventId, ct);
            return persisted is not null
                && persisted.State != IdentityAuditOutboxState.Poisoned
                && AuditEventV1ReadBack.Matches(auditEvent, ToReadBack(persisted.Event));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return false;
        }
    }

    private static AuditEventReadBackV1 ToReadBack(AuditEventV1 auditEvent) => new(
        auditEvent.EventId,
        auditEvent.ActorKind,
        auditEvent.ActorId,
        auditEvent.SubjectId,
        auditEvent.WorkspaceId,
        auditEvent.Action,
        auditEvent.TargetType,
        auditEvent.TargetId,
        auditEvent.Outcome,
        auditEvent.OccurredAt,
        auditEvent.CorrelationId,
        auditEvent.Metadata ?? new Dictionary<string, string>());

    private static Result<WorkspaceInvitationExchangeDto> Invalid() =>
        Result.Failure<WorkspaceInvitationExchangeDto>(
            ErrorCodes.InvalidInput,
            "Invitation access is invalid or no longer available.",
            IdentityProblemCodes.InvitationAccessInvalid);

    private static Result<WorkspaceInvitationExchangeDto> ReadBackFailure() =>
        Result.Failure<WorkspaceInvitationExchangeDto>(
            ErrorCodes.BusinessRule,
            "Invitation exchange could not be confirmed.",
            IdentityProblemCodes.InvitationReadBackFailed);

    private static Result<WorkspaceInvitationExchangeDto> AuditUnavailable() =>
        Result.Failure<WorkspaceInvitationExchangeDto>(
            ErrorCodes.BusinessRule,
            "Invitation outcome could not be audited.",
            IdentityProblemCodes.InvitationAuditUnavailable);
}
