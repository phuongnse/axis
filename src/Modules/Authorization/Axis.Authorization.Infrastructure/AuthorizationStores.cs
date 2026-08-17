using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Axis.Authorization.Infrastructure;

internal sealed class ProductRoleAssignmentStore(AuthorizationDbContext context)
    : IProductRoleAssignmentStore
{
    public async Task<StoredProductRoleAssignment?> GetAsync(
        Guid workspaceId,
        SubjectReference subject,
        Guid policyVersionId,
        string roleKey,
        CancellationToken cancellationToken = default)
    {
        ProductRoleAssignmentRow? row = await context.Assignments.SingleOrDefaultAsync(
            value => value.WorkspaceId == workspaceId
                && value.SubjectKind == subject.Kind.ToString()
                && value.SubjectId == subject.Id
                && value.PolicyVersionId == policyVersionId
                && value.RoleKey == roleKey,
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<StoredProductRoleAssignment?> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        ProductRoleAssignmentRow? row = await context.Assignments.SingleOrDefaultAsync(
            value => value.Id == assignmentId,
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<ProductRoleIdempotencyRecord?> GetIdempotencyAsync(
        Guid workspaceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        AuthorizationIdempotencyRow? row = await context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.WorkspaceId == workspaceId
                    && value.IdempotencyKey == idempotencyKey,
                cancellationToken);
        return row is null
            ? null
            : new(
                row.WorkspaceId,
                row.IdempotencyKey,
                row.RequestDigest,
                row.Operation,
                row.AssignmentId,
                row.AuditEventId,
                row.CreatedAt);
    }

    public Task AddAsync(
        StoredProductRoleAssignment value,
        CancellationToken cancellationToken = default) =>
        context.Assignments.AddAsync(
            new ProductRoleAssignmentRow
            {
                Id = value.Id,
                WorkspaceId = value.WorkspaceId,
                SubjectKind = value.Subject.Kind.ToString(),
                SubjectId = value.Subject.Id,
                PolicyVersionId = value.PolicyVersionId,
                RoleKey = value.RoleKey,
                IsActive = value.IsActive,
                Revision = value.Revision,
                CreatedAt = value.CreatedAt,
                RevokedAt = value.RevokedAt,
                UpdatedAt = value.UpdatedAt,
                CreatedByKind = value.CreatedBy?.Kind.ToString(),
                CreatedBySubjectId = value.CreatedBy?.SubjectId,
                CreatedByDisplayName = value.CreatedBy?.DisplayName,
                UpdatedByKind = value.UpdatedBy?.Kind.ToString(),
                UpdatedBySubjectId = value.UpdatedBy?.SubjectId,
                UpdatedByDisplayName = value.UpdatedBy?.DisplayName,
            },
            cancellationToken).AsTask();

    public async Task SaveAsync(
        StoredProductRoleAssignment value,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ProductRoleAssignmentRow row = await context.Assignments.SingleAsync(
            candidate => candidate.Id == value.Id,
            cancellationToken);
        if (row.Revision != expectedRevision)
            throw new AuthorizationPersistenceConflictException(
                "The product-role assignment revision changed.");

        row.IsActive = value.IsActive;
        row.Revision = value.Revision;
        row.RevokedAt = value.RevokedAt;
        row.UpdatedAt = value.UpdatedAt;
        row.UpdatedByKind = value.UpdatedBy?.Kind.ToString();
        row.UpdatedBySubjectId = value.UpdatedBy?.SubjectId;
        row.UpdatedByDisplayName = value.UpdatedBy?.DisplayName;
    }

    public Task AddIdempotencyAsync(
        ProductRoleIdempotencyRecord record,
        CancellationToken cancellationToken = default) =>
        context.IdempotencyRecords.AddAsync(
            new AuthorizationIdempotencyRow
            {
                WorkspaceId = record.WorkspaceId,
                IdempotencyKey = record.IdempotencyKey,
                RequestDigest = record.RequestDigest,
                Operation = record.Operation,
                AssignmentId = record.AssignmentId,
                AuditEventId = record.AuditEventId,
                CreatedAt = record.CreatedAt,
            },
            cancellationToken).AsTask();

    public async Task<IReadOnlyList<StoredProductRoleAssignment>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        (await context.Assignments.AsNoTracking()
            .Where(value => value.WorkspaceId == workspaceId)
            .OrderBy(value => value.SubjectKind)
            .ThenBy(value => value.SubjectId)
            .ThenBy(value => value.RoleKey)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();

    private static StoredProductRoleAssignment Map(ProductRoleAssignmentRow row) =>
        new(
            row.Id,
            row.WorkspaceId,
            new SubjectReference(Enum.Parse<SubjectKind>(row.SubjectKind), row.SubjectId),
            row.PolicyVersionId,
            row.RoleKey,
            row.IsActive,
            row.Revision,
            row.CreatedAt,
            row.RevokedAt,
            row.UpdatedAt,
            Actor(row.CreatedByKind, row.CreatedBySubjectId, row.CreatedByDisplayName),
            Actor(row.UpdatedByKind, row.UpdatedBySubjectId, row.UpdatedByDisplayName));

    private static ActorSnapshot? Actor(
        string? kind,
        Guid? subjectId,
        string? displayName)
    {
        if (!Enum.TryParse(kind, out ActorKind actorKind)
            || string.IsNullOrWhiteSpace(displayName))
            return null;

        ActorSnapshot actor = new(actorKind, subjectId, displayName);
        return actor.IsValid ? actor : null;
    }
}

internal sealed class InstalledProductRoleStore(AuthorizationDbContext context)
    : IInstalledProductRoleStore
{
    public async Task<bool> ExistsAsync(
        Guid workspaceId,
        Guid policyVersionId,
        string roleKey,
        CancellationToken cancellationToken = default)
    {
        string? content = await context.Policies
            .AsNoTracking()
            .Where(value => value.WorkspaceId == workspaceId
                && value.VersionId == policyVersionId)
            .Select(value => value.CanonicalContent)
            .SingleOrDefaultAsync(cancellationToken);
        if (content is null)
            return false;

        try
        {
            ProductPolicyComponent? component = JsonSerializer.Deserialize<ProductPolicyComponent>(
                content,
                ProductPolicyJson.Options);
            return component?.Roles.Any(role => StringComparer.Ordinal.Equals(role.RoleKey, roleKey)) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed class AuthorizationUnitOfWork(AuthorizationDbContext context)
    : IAuthorizationUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("An Authorization transaction is already active.");
        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new AuthorizationPersistenceConflictException(
                "Authorization persistence conflicted with current state.",
                exception);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction transaction = _transaction
            ?? throw new InvalidOperationException("No Authorization transaction is active.");
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            return;
        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
        context.ChangeTracker.Clear();
    }
}

internal sealed class AuthorizationAuditOutbox(
    AuthorizationDbContext context,
    TimeProvider clock) : IAuthorizationAuditSink
{
    public async Task<AuditIngestionResult> IngestAsync(
        AuditEventV1 entry,
        CancellationToken cancellationToken = default)
    {
        AuditEventValidationResult validation = AuditEventV1Validator.Validate(entry);
        if (!validation.IsValid)
            return new(AuditIngestionDisposition.Rejected, null, validation.RejectionCode);

        AuditEventV1 persistedEntry = entry with
        {
            OccurredAt = entry.OccurredAt.AddTicks(-(entry.OccurredAt.Ticks % 10)),
            CorrelationId = entry.CorrelationId.Trim(),
        };

        AuthorizationAuditOutboxRow? existing = context.AuditOutbox.Local
            .SingleOrDefault(value => value.Id == entry.EventId)
            ?? await context.AuditOutbox.SingleOrDefaultAsync(
                value => value.Id == entry.EventId,
                cancellationToken);
        if (existing is not null)
        {
            AuditEventReadBackV1? readBack = Deserialize(existing.Payload);
            return readBack is not null && AuditEventV1ReadBack.Matches(entry, readBack)
                ? new(AuditIngestionDisposition.AlreadyStored, readBack)
                : new(AuditIngestionDisposition.Conflict, null, "audit.event_conflict");
        }

        DateTimeOffset now = clock.GetUtcNow();
        await context.AuditOutbox.AddAsync(
            new AuthorizationAuditOutboxRow
            {
                Id = entry.EventId,
                OccurredAt = persistedEntry.OccurredAt,
                Payload = JsonSerializer.Serialize(persistedEntry),
                DeliveryState = "Pending",
                NextAttemptAt = now,
                CreatedAt = now,
            },
            cancellationToken);
        return new(AuditIngestionDisposition.Stored, null);
    }

    public async Task<AuditEventReadBackV1?> ReadBackAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        AuthorizationAuditOutboxRow? row = await context.AuditOutbox.SingleOrDefaultAsync(
            value => value.Id == eventId,
            cancellationToken);
        if (row is null)
            return null;

        AuditEventReadBackV1? readBack = Deserialize(row.Payload);
        if (readBack is not null)
            row.ReadBackAt = clock.GetUtcNow();
        return readBack;
    }

    private static AuditEventReadBackV1? Deserialize(string payload)
    {
        try
        {
            AuditEventV1? entry = JsonSerializer.Deserialize<AuditEventV1>(payload);
            return entry is null
                ? null
                : new(
                    entry.EventId,
                    entry.ActorKind,
                    entry.ActorId,
                    entry.SubjectId,
                    entry.WorkspaceId,
                    entry.Action,
                    entry.TargetType,
                    entry.TargetId,
                    entry.Outcome,
                    entry.OccurredAt,
                    entry.CorrelationId,
                    entry.Metadata ?? new Dictionary<string, string>());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
