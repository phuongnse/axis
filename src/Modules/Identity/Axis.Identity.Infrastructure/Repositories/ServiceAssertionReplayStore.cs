using Axis.Audit.Contracts;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class ServiceAssertionReplayStore(
    IdentityDbContext context,
    IIdentityAuditOutbox audit,
    TimeProvider clock) : IServiceAssertionReplayStore
{
    public async Task<bool> TryAcceptAsync(
        string digest,
        DateTime expiresAt,
        AuditEventV1 successAudit,
        AuditEventV1 replayAudit,
        CancellationToken ct = default)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            DateTime now = clock.GetUtcNow().UtcDateTime;
            await PurgeExpiredAsync(now, ct);
            await context.ServiceAssertionReplayRecords.AddAsync(
                new ServiceAssertionReplayRecord
                {
                    Digest = digest,
                    ExpiresAt = expiresAt,
                    CreatedAt = now,
                },
                ct);
            await audit.EnqueueAsync(successAudit, ct);
            await context.SaveChangesAsync(ct);
            if (!await HasExactAuditAsync(successAudit, ct))
                throw new InvalidOperationException("Service authentication audit read-back failed.");
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_service_assertion_replays",
            })
        {
            await transaction.RollbackAsync(ct);
            context.ChangeTracker.Clear();
        }

        await RecordAuditAsync(replayAudit, ct);
        return false;
    }

    public async Task RecordAuditAsync(
        AuditEventV1 auditEvent,
        CancellationToken ct = default)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            await audit.EnqueueAsync(auditEvent, ct);
            await context.SaveChangesAsync(ct);
            if (!await HasExactAuditAsync(auditEvent, ct))
                throw new InvalidOperationException("Service authentication audit read-back failed.");
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public Task PurgeExpiredAsync(DateTime now, CancellationToken ct = default) =>
        context.ServiceAssertionReplayRecords
            .Where(value => value.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);

    private async Task<bool> HasExactAuditAsync(
        AuditEventV1 expected,
        CancellationToken ct)
    {
        IdentityAuditOutboxEntry? readBack = await audit.GetAsync(expected.EventId, ct);
        if (readBack is null)
            return false;

        AuditEventV1 actual = readBack.Event;
        return AuditEventV1ReadBack.Matches(
            expected,
            new AuditEventReadBackV1(
                actual.EventId,
                actual.ActorKind,
                actual.ActorId,
                actual.SubjectId,
                actual.WorkspaceId,
                actual.Action,
                actual.TargetType,
                actual.TargetId,
                actual.Outcome,
                actual.OccurredAt,
                actual.CorrelationId,
                actual.Metadata ?? new Dictionary<string, string>()));
    }
}
