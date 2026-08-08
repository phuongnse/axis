using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Authorization.Infrastructure.Repositories;

internal sealed class AuthorizationAuditDispatchStore(AuthorizationDbContext context)
    : IAuthorizationAuditDispatchStore
{
    private const int FailureReasonMaximumLength = 256;

    public async Task<IReadOnlyList<AuthorizationAuditDispatchItem>> ClaimDueBatchAsync(
        DateTimeOffset now,
        TimeSpan leaseLifetime,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (leaseLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseLifetime));
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        List<Guid> candidates = await context.AuditOutbox
            .AsNoTracking()
            .Where(row =>
                row.DeliveryState == "Pending"
                && row.NextAttemptAt <= now
                && (row.LeaseUntil == null || row.LeaseUntil <= now))
            .OrderBy(row => row.NextAttemptAt)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        List<AuthorizationAuditDispatchItem> claimed = [];
        foreach (Guid eventId in candidates)
        {
            Guid leaseId = Guid.NewGuid();
            int updated = await context.AuditOutbox
                .Where(row =>
                    row.Id == eventId
                    && row.DeliveryState == "Pending"
                    && row.NextAttemptAt <= now
                    && (row.LeaseUntil == null || row.LeaseUntil <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.LeaseId, leaseId)
                    .SetProperty(row => row.LeaseUntil, now.Add(leaseLifetime))
                    .SetProperty(row => row.LastAttemptAt, now)
                    .SetProperty(row => row.AttemptCount, row => row.AttemptCount + 1)
                    .SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            if (updated != 1)
                continue;

            AuthorizationAuditOutboxRow row = await context.AuditOutbox
                .AsNoTracking()
                .SingleAsync(item => item.Id == eventId && item.LeaseId == leaseId, cancellationToken);
            AuditEventV1? auditEvent = Deserialize(row.Payload);
            if (auditEvent is null)
            {
                await MarkPoisonedAsync(
                    eventId,
                    leaseId,
                    "audit.payload_invalid",
                    cancellationToken);
                continue;
            }

            claimed.Add(new AuthorizationAuditDispatchItem(auditEvent, row.AttemptCount, leaseId));
        }

        return claimed;
    }

    public async Task<bool> MarkDeliveredAsync(
        Guid eventId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        int updated = await MatchingLease(eventId, leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.DeliveryState, "Delivered")
                .SetProperty(row => row.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(row => row.LeaseId, (Guid?)null)
                .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(row => row.FailureReason, (string?)null)
                .SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
        return updated == 1;
    }

    public Task<bool> MarkForRetryAsync(
        Guid eventId,
        Guid leaseId,
        DateTimeOffset nextAttemptAt,
        string failureReason,
        CancellationToken cancellationToken = default) =>
        UpdateFailureAsync(
            eventId,
            leaseId,
            "Pending",
            nextAttemptAt,
            failureReason,
            cancellationToken);

    public Task<bool> MarkPoisonedAsync(
        Guid eventId,
        Guid leaseId,
        string failureReason,
        CancellationToken cancellationToken = default) =>
        UpdateFailureAsync(
            eventId,
            leaseId,
            "Poisoned",
            null,
            failureReason,
            cancellationToken);

    private async Task<bool> UpdateFailureAsync(
        Guid eventId,
        Guid leaseId,
        string deliveryState,
        DateTimeOffset? nextAttemptAt,
        string failureReason,
        CancellationToken cancellationToken)
    {
        string boundedReason = failureReason.Length <= FailureReasonMaximumLength
            ? failureReason
            : failureReason[..FailureReasonMaximumLength];
        int updated = await MatchingLease(eventId, leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.DeliveryState, deliveryState)
                .SetProperty(row => row.NextAttemptAt, nextAttemptAt)
                .SetProperty(row => row.LeaseId, (Guid?)null)
                .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(row => row.FailureReason, boundedReason)
                .SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
        return updated == 1;
    }

    private IQueryable<AuthorizationAuditOutboxRow> MatchingLease(Guid eventId, Guid leaseId) =>
        context.AuditOutbox.Where(row =>
            row.Id == eventId
            && row.DeliveryState == "Pending"
            && row.LeaseId == leaseId);

    private static AuditEventV1? Deserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<AuditEventV1>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
