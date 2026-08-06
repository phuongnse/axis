using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class IdentityAuditDispatchStore(IdentityDbContext context)
    : IIdentityAuditDispatchStore
{
    private const int FailureReasonMaximumLength = 256;

    public async Task<IReadOnlyList<IdentityAuditDispatchItem>> ClaimDueBatchAsync(
        DateTimeOffset now,
        TimeSpan leaseLifetime,
        int batchSize,
        CancellationToken ct = default)
    {
        if (leaseLifetime <= TimeSpan.Zero || batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        List<Guid> candidates = await context.IdentityAuditOutboxRecords
            .AsNoTracking()
            .Where(record =>
                record.Status == IdentityAuditOutboxStatus.Pending
                && record.NextAttemptAt <= now
                && (record.LeaseUntil == null || record.LeaseUntil <= now))
            .OrderBy(record => record.NextAttemptAt)
            .ThenBy(record => record.CreatedAt)
            .ThenBy(record => record.EventId)
            .Select(record => record.EventId)
            .Take(batchSize)
            .ToListAsync(ct);

        List<IdentityAuditDispatchItem> claimed = [];
        foreach (Guid eventId in candidates)
        {
            Guid leaseId = Guid.NewGuid();
            int updated = await context.IdentityAuditOutboxRecords
                .Where(record =>
                    record.EventId == eventId
                    && record.Status == IdentityAuditOutboxStatus.Pending
                    && record.NextAttemptAt <= now
                    && (record.LeaseUntil == null || record.LeaseUntil <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(record => record.LeaseId, leaseId)
                    .SetProperty(record => record.LeaseUntil, now.Add(leaseLifetime))
                    .SetProperty(record => record.LastAttemptAt, now)
                    .SetProperty(record => record.AttemptCount, record => record.AttemptCount + 1)
                    .SetProperty(record => record.Revision, record => record.Revision + 1), ct);
            if (updated != 1)
                continue;

            IdentityAuditOutboxRecord record = await context.IdentityAuditOutboxRecords
                .AsNoTracking()
                .SingleAsync(item => item.EventId == eventId && item.LeaseId == leaseId, ct);
            claimed.Add(new IdentityAuditDispatchItem(
                record.ToAuditEvent(),
                record.AttemptCount,
                leaseId));
        }

        return claimed;
    }

    public async Task<bool> MarkDeliveredAsync(
        Guid eventId,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(ct);
        int updated = await MatchingLease(eventId, leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.Status, IdentityAuditOutboxStatus.Delivered)
                .SetProperty(record => record.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(record => record.LeaseId, (Guid?)null)
                .SetProperty(record => record.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(record => record.FailureReason, (string?)null)
                .SetProperty(record => record.Revision, record => record.Revision + 1), ct);
        if (updated == 1)
        {
            await context.WorkspaceContextTransitions
                .Where(transition =>
                    transition.TerminalAuditEventId == eventId
                    && transition.Status != WorkspaceContextTransitionStatus.Pending
                    && transition.AuditProjectionConfirmedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(transition => transition.AuditProjectionConfirmedAt, now.UtcDateTime)
                    .SetProperty(transition => transition.Revision, transition => transition.Revision + 1), ct);
        }

        await transaction.CommitAsync(ct);
        return updated == 1;
    }

    public Task<bool> MarkForRetryAsync(
        Guid eventId,
        Guid leaseId,
        DateTimeOffset nextAttemptAt,
        string failureReason,
        CancellationToken ct = default) =>
        UpdateFailureAsync(
            eventId,
            leaseId,
            IdentityAuditOutboxStatus.Pending,
            nextAttemptAt,
            failureReason,
            ct);

    public Task<bool> MarkPoisonedAsync(
        Guid eventId,
        Guid leaseId,
        string failureReason,
        CancellationToken ct = default) =>
        UpdateFailureAsync(
            eventId,
            leaseId,
            IdentityAuditOutboxStatus.Poisoned,
            null,
            failureReason,
            ct);

    private async Task<bool> UpdateFailureAsync(
        Guid eventId,
        Guid leaseId,
        IdentityAuditOutboxStatus status,
        DateTimeOffset? nextAttemptAt,
        string failureReason,
        CancellationToken ct)
    {
        string boundedReason = failureReason.Length <= FailureReasonMaximumLength
            ? failureReason
            : failureReason[..FailureReasonMaximumLength];
        int updated = await MatchingLease(eventId, leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.Status, status)
                .SetProperty(record => record.NextAttemptAt, nextAttemptAt)
                .SetProperty(record => record.LeaseId, (Guid?)null)
                .SetProperty(record => record.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(record => record.FailureReason, boundedReason)
                .SetProperty(record => record.Revision, record => record.Revision + 1), ct);
        return updated == 1;
    }

    private IQueryable<IdentityAuditOutboxRecord> MatchingLease(Guid eventId, Guid leaseId) =>
        context.IdentityAuditOutboxRecords.Where(record =>
            record.EventId == eventId
            && record.Status == IdentityAuditOutboxStatus.Pending
            && record.LeaseId == leaseId);
}
