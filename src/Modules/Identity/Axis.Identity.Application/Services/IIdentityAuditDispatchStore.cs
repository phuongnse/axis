using Axis.Audit.Contracts;

namespace Axis.Identity.Application.Services;

public interface IIdentityAuditDispatchStore
{
    Task<IReadOnlyList<IdentityAuditDispatchItem>> ClaimDueBatchAsync(
        DateTimeOffset now,
        TimeSpan leaseLifetime,
        int batchSize,
        CancellationToken ct = default);

    Task<bool> MarkDeliveredAsync(
        Guid eventId,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<bool> MarkForRetryAsync(
        Guid eventId,
        Guid leaseId,
        DateTimeOffset nextAttemptAt,
        string failureReason,
        CancellationToken ct = default);

    Task<bool> MarkPoisonedAsync(
        Guid eventId,
        Guid leaseId,
        string failureReason,
        CancellationToken ct = default);
}

public sealed record IdentityAuditDispatchItem(
    AuditEventV1 Event,
    int AttemptCount,
    Guid LeaseId);
