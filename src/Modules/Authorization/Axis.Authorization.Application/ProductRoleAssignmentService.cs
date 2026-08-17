using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;

namespace Axis.Authorization.Application;

public interface IAuthorizationSubjectActivity
{
    Task<bool> IsActiveAsync(
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken = default);
}

public interface IAuthorizationAdministratorAuthority
{
    Task<bool> IsAdministratorAsync(
        Guid workspaceId,
        SubjectReference actor,
        CancellationToken cancellationToken = default);
}

public interface IInstalledProductRoleStore
{
    Task<bool> ExistsAsync(
        Guid workspaceId,
        Guid policyVersionId,
        string roleKey,
        CancellationToken cancellationToken = default);
}

public sealed record StoredProductRoleAssignment(
    Guid Id,
    Guid WorkspaceId,
    SubjectReference Subject,
    Guid PolicyVersionId,
    string RoleKey,
    bool IsActive,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? UpdatedAt = null,
    ActorSnapshot? CreatedBy = null,
    ActorSnapshot? UpdatedBy = null)
{
    public ProductRoleAssignment ToContract() =>
        new(WorkspaceId, Subject, PolicyVersionId, RoleKey, IsActive, Revision);
}

public sealed record ProductRoleIdempotencyRecord(
    Guid WorkspaceId,
    string IdempotencyKey,
    string RequestDigest,
    string Operation,
    Guid AssignmentId,
    Guid AuditEventId,
    DateTimeOffset CreatedAt);

public interface IProductRoleAssignmentStore
{
    Task<StoredProductRoleAssignment?> GetAsync(
        Guid workspaceId,
        SubjectReference subject,
        Guid policyVersionId,
        string roleKey,
        CancellationToken cancellationToken = default);

    Task<StoredProductRoleAssignment?> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    Task<ProductRoleIdempotencyRecord?> GetIdempotencyAsync(
        Guid workspaceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        StoredProductRoleAssignment assignment,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        StoredProductRoleAssignment assignment,
        int expectedRevision,
        CancellationToken cancellationToken = default);

    Task AddIdempotencyAsync(
        ProductRoleIdempotencyRecord record,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredProductRoleAssignment>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredProductRoleAssignment>>([]);
}

public interface IAuthorizationUnitOfWork
{
    Task BeginAsync(CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthorizationPersistenceConflictException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed record AssignProductRoleRequest(
    Guid WorkspaceId,
    SubjectReference Actor,
    SubjectReference Target,
    Guid PolicyVersionId,
    string RoleKey,
    string IdempotencyKey,
    string CorrelationId,
    string ActorDisplayName,
    int? ExpectedRevision = null);

public sealed record RevokeProductRoleRequest(
    Guid WorkspaceId,
    SubjectReference Actor,
    SubjectReference Target,
    Guid PolicyVersionId,
    string RoleKey,
    string IdempotencyKey,
    string CorrelationId,
    string ActorDisplayName,
    int ExpectedRevision);

public sealed record ProductRoleAssignmentResult(
    bool IsSuccess,
    ProductRoleAssignment? Assignment,
    string? Error);

public sealed class ProductRoleAssignmentService(
    IAuthorizationSubjectActivity activity,
    IAuthorizationAdministratorAuthority administrators,
    IInstalledProductRoleStore installedRoles,
    IProductRoleAssignmentStore assignments,
    IAuthorizationAuditSink audit,
    IAuthorizationUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public Task<ProductRoleAssignmentResult> AssignAsync(
        AssignProductRoleRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "assign",
            request.WorkspaceId,
            request.Actor,
            request.Target,
            request.PolicyVersionId,
            request.RoleKey,
            request.IdempotencyKey,
            request.CorrelationId,
            request.ActorDisplayName,
            request.ExpectedRevision,
            cancellationToken);

    public Task<ProductRoleAssignmentResult> RevokeAsync(
        RevokeProductRoleRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "revoke",
            request.WorkspaceId,
            request.Actor,
            request.Target,
            request.PolicyVersionId,
            request.RoleKey,
            request.IdempotencyKey,
            request.CorrelationId,
            request.ActorDisplayName,
            request.ExpectedRevision,
            cancellationToken);

    private async Task<ProductRoleAssignmentResult> ExecuteAsync(
        string operation,
        Guid workspaceId,
        SubjectReference actor,
        SubjectReference target,
        Guid policyVersionId,
        string roleKey,
        string idempotencyKey,
        string correlationId,
        string actorDisplayName,
        int? expectedRevision,
        CancellationToken cancellationToken)
    {
        roleKey = roleKey?.Trim() ?? string.Empty;
        idempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        correlationId = correlationId?.Trim() ?? string.Empty;
        actorDisplayName = actorDisplayName?.Trim() ?? string.Empty;
        if (workspaceId == Guid.Empty
            || actor.Kind != SubjectKind.Human
            || actor.Id == Guid.Empty
            || target.Id == Guid.Empty
            || !Enum.IsDefined(target.Kind)
            || policyVersionId == Guid.Empty
            || roleKey.Length is 0 or > 200
            || idempotencyKey.Length is 0 or > 120
            || correlationId.Length is 0 or > AuditEventV1Validator.MaximumCorrelationIdLength
            || actorDisplayName.Length is 0 or > ActorSnapshot.MaximumDisplayNameLength
            || (operation == "revoke" && expectedRevision is null))
        {
            return await AuditInvalidRequestAsync(
                operation,
                workspaceId,
                actor,
                target,
                correlationId,
                cancellationToken);
        }

        if (!await administrators.IsAdministratorAsync(workspaceId, actor, cancellationToken))
            return await AuditDenialAsync("authority_denied", workspaceId, actor, target, policyVersionId, roleKey, correlationId, cancellationToken);

        if (!await activity.IsActiveAsync(workspaceId, target, cancellationToken))
            return await AuditDenialAsync("target_inactive", workspaceId, actor, target, policyVersionId, roleKey, correlationId, cancellationToken);

        if (!await installedRoles.ExistsAsync(workspaceId, policyVersionId, roleKey, cancellationToken))
            return await AuditDenialAsync("role_unavailable", workspaceId, actor, target, policyVersionId, roleKey, correlationId, cancellationToken);

        string digest = RequestDigest(
            operation,
            workspaceId,
            actor,
            target,
            policyVersionId,
            roleKey);

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            ProductRoleIdempotencyRecord? retry = await assignments.GetIdempotencyAsync(
                workspaceId,
                idempotencyKey,
                cancellationToken);
            if (retry is not null)
            {
                if (!StringComparer.Ordinal.Equals(retry.RequestDigest, digest))
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return await AuditDenialAsync("idempotency_conflict", workspaceId, actor, target, policyVersionId, roleKey, correlationId, cancellationToken);
                }

                StoredProductRoleAssignment? canonical = await assignments.GetByIdAsync(
                    retry.AssignmentId,
                    cancellationToken);
                AuditEventReadBackV1? originalAudit = await audit.ReadBackAsync(
                    retry.AuditEventId,
                    cancellationToken);
                if (canonical is null || originalAudit is null)
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return new(false, null, "audit_unavailable");
                }

                await unitOfWork.RollbackAsync(cancellationToken);
                return new(true, canonical.ToContract(), null);
            }

            StoredProductRoleAssignment? current = await assignments.GetAsync(
                workspaceId,
                target,
                policyVersionId,
                roleKey,
                cancellationToken);

            if (expectedRevision is int expected && current?.Revision != expected)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return await AuditDenialAsync("revision_conflict", workspaceId, actor, target, policyVersionId, roleKey, correlationId, cancellationToken);
            }

            if (operation == "revoke" && (current is null || !current.IsActive))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return await AuditDenialAsync("assignment_inactive", workspaceId, actor, target, policyVersionId, roleKey, correlationId, cancellationToken);
            }

            DateTimeOffset now = clock.GetUtcNow();
            ActorSnapshot actorSnapshot = ActorSnapshot.User(actor.Id, actorDisplayName);
            StoredProductRoleAssignment changed = current switch
            {
                null => new(
                    Guid.NewGuid(),
                    workspaceId,
                    target,
                    policyVersionId,
                    roleKey,
                    IsActive: true,
                    Revision: 1,
                    CreatedAt: now,
                    RevokedAt: null,
                    UpdatedAt: now,
                    CreatedBy: actorSnapshot,
                    UpdatedBy: actorSnapshot),
                { IsActive: true } when operation == "assign" => current,
                _ when operation == "assign" => current with
                {
                    IsActive = true,
                    Revision = current.Revision + 1,
                    RevokedAt = null,
                    UpdatedAt = now,
                    UpdatedBy = actorSnapshot,
                },
                _ => current with
                {
                    IsActive = false,
                    Revision = current.Revision + 1,
                    RevokedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = actorSnapshot,
                },
            };

            string outcome = operation == "assign" ? "assigned" : "revoked";
            Guid auditEventId = DeterministicEventId(digest, idempotencyKey, outcome);
            AuditEventV1 auditEvent = Event(
                auditEventId,
                outcome,
                workspaceId,
                actor,
                target,
                policyVersionId,
                roleKey,
                correlationId,
                now);
            AuditIngestionResult ingestion = await audit.IngestAsync(auditEvent, cancellationToken);
            if (ingestion.Disposition is AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return new(false, null, "audit_unavailable");
            }

            if (current is null)
                await assignments.AddAsync(changed, cancellationToken);
            else if (changed != current)
                await assignments.SaveAsync(changed, current.Revision, cancellationToken);

            await assignments.AddIdempotencyAsync(
                new(
                    workspaceId,
                    idempotencyKey,
                    digest,
                    operation,
                    changed.Id,
                    auditEventId,
                    now),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            AuditEventReadBackV1? readBack = await audit.ReadBackAsync(auditEventId, cancellationToken);
            if (readBack is null || !AuditEventV1ReadBack.Matches(auditEvent, readBack))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return new(false, null, "audit_unavailable");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new(true, changed.ToContract(), null);
        }
        catch (AuthorizationPersistenceConflictException)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, null, "conflict");
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, null, "unavailable");
        }
    }

    private async Task<ProductRoleAssignmentResult> AuditDenialAsync(
        string outcome,
        Guid workspaceId,
        SubjectReference actor,
        SubjectReference target,
        Guid policyVersionId,
        string roleKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AuditEventV1 auditEvent = Event(
            Guid.NewGuid(),
            outcome,
            workspaceId,
            actor,
            target,
            policyVersionId,
            roleKey,
            correlationId,
            clock.GetUtcNow());
        return await PersistDenialAsync(auditEvent, outcome, cancellationToken);
    }

    private async Task<ProductRoleAssignmentResult> AuditInvalidRequestAsync(
        string operation,
        Guid workspaceId,
        SubjectReference actor,
        SubjectReference target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        Guid eventId = Guid.NewGuid();
        bool hasActor = workspaceId != Guid.Empty
            && actor.Kind == SubjectKind.Human
            && actor.Id != Guid.Empty;
        bool hasTarget = target.Id != Guid.Empty && Enum.IsDefined(target.Kind);
        AuditEventV1 auditEvent = new(
            eventId,
            hasActor ? AuditActorKindV1.Human : AuditActorKindV1.Anonymous,
            hasActor ? actor.Id : null,
            hasActor ? hasTarget ? target.Id : actor.Id : null,
            workspaceId == Guid.Empty ? null : workspaceId,
            "authorization.assignment",
            "product-role",
            InvalidAssignmentTargetId(operation, workspaceId),
            "invalid_request",
            clock.GetUtcNow(),
            correlationId.Length is > 0 and <= AuditEventV1Validator.MaximumCorrelationIdLength
                ? correlationId
                : $"authorization-{eventId:N}",
            new Dictionary<string, string> { ["request"] = "invalid" });
        return await PersistDenialAsync(auditEvent, "invalid", cancellationToken);
    }

    private async Task<ProductRoleAssignmentResult> PersistDenialAsync(
        AuditEventV1 auditEvent,
        string outcome,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            AuditIngestionResult ingestion = await audit.IngestAsync(auditEvent, cancellationToken);
            if (ingestion.Disposition is AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return new(false, null, "audit_unavailable");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            AuditEventReadBackV1? readBack = await audit.ReadBackAsync(auditEvent.EventId, cancellationToken);
            if (readBack is null || !AuditEventV1ReadBack.Matches(auditEvent, readBack))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return new(false, null, "audit_unavailable");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new(false, null, outcome);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return new(false, null, "audit_unavailable");
        }
    }

    private static AuditEventV1 Event(
        Guid eventId,
        string outcome,
        Guid workspaceId,
        SubjectReference actor,
        SubjectReference target,
        Guid policyVersionId,
        string roleKey,
        string correlationId,
        DateTimeOffset occurredAt) =>
        new(
            eventId,
            AuditActorKindV1.Human,
            actor.Id,
            target.Id,
            workspaceId,
            "authorization.assignment",
            "product-role",
            policyVersionId,
            outcome,
            occurredAt,
            correlationId,
            new Dictionary<string, string> { ["role"] = roleKey });

    private static string RequestDigest(
        string operation,
        Guid workspaceId,
        SubjectReference actor,
        SubjectReference target,
        Guid policyVersionId,
        string roleKey)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            operation,
            workspaceId,
            actorKind = actor.Kind.ToString(),
            actorId = actor.Id,
            targetKind = target.Kind.ToString(),
            targetId = target.Id,
            policyVersionId,
            roleKey,
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static Guid DeterministicEventId(
        string digest,
        string idempotencyKey,
        string outcome)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{digest}:{idempotencyKey}:{outcome}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static Guid InvalidAssignmentTargetId(string operation, Guid workspaceId)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                workspaceId == Guid.Empty
                    ? $"authorization.assignment.invalid:{operation}"
                    : $"authorization.assignment.invalid:{operation}:{workspaceId:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
