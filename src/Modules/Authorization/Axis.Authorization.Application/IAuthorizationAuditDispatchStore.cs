using Axis.Audit.Contracts;

namespace Axis.Authorization.Application;

public interface IAuthorizationAuditDispatchStore
{
    Task<IReadOnlyList<AuthorizationAuditDispatchItem>> ClaimDueBatchAsync(
        DateTimeOffset now,
        TimeSpan leaseLifetime,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<bool> MarkDeliveredAsync(
        Guid eventId,
        Guid leaseId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkForRetryAsync(
        Guid eventId,
        Guid leaseId,
        DateTimeOffset nextAttemptAt,
        string failureReason,
        CancellationToken cancellationToken = default);

    Task<bool> MarkPoisonedAsync(
        Guid eventId,
        Guid leaseId,
        string failureReason,
        CancellationToken cancellationToken = default);
}

public sealed record AuthorizationAuditDispatchItem(
    AuditEventV1 Event,
    int AttemptCount,
    Guid LeaseId);
