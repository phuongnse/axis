using Axis.Audit.Contracts;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Solutions.Infrastructure.Services;

public sealed record SolutionsAuditOutboxHealth(
    int Pending,
    int Retrying,
    int Delivered,
    int Poisoned,
    DateTimeOffset? OldestPendingAt = null);

/// <summary>Host-composable dispatcher for redacted durable audit records.</summary>
public sealed class SolutionsAuditDispatchWorker(IServiceScopeFactory scopes, TimeProvider clock)
{
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

    public async Task<SolutionsAuditOutboxHealth> DispatchAsync(int maximumCount, CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        SolutionsDbContext db = scope.ServiceProvider.GetRequiredService<SolutionsDbContext>();
        IAuditEventSink sink = scope.ServiceProvider.GetRequiredService<IAuditEventSink>();
        DateTimeOffset now = clock.GetUtcNow();
        List<Guid> due = await db.AuditOutbox
            .AsNoTracking()
            .Where(value =>
                ((value.Status == "Pending" || value.Status == "Retrying")
                    && (value.NextAttemptAt == null || value.NextAttemptAt <= now)
                    && (value.LeaseUntil == null || value.LeaseUntil <= now))
                || (value.Status == "Delivering" && value.LeaseUntil <= now))
            .OrderBy(value => value.NextAttemptAt)
            .ThenBy(value => value.CreatedAt)
            .ThenBy(value => value.EventId)
            .Select(value => value.EventId)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
        foreach (Guid eventId in due)
        {
            Guid leaseId = Guid.NewGuid();
            int claimed = await db.AuditOutbox
                .Where(value =>
                    value.EventId == eventId
                    && (((value.Status == "Pending" || value.Status == "Retrying")
                            && (value.NextAttemptAt == null || value.NextAttemptAt <= now)
                            && (value.LeaseUntil == null || value.LeaseUntil <= now))
                        || (value.Status == "Delivering" && value.LeaseUntil <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, "Delivering")
                    .SetProperty(value => value.LeaseId, leaseId)
                    .SetProperty(value => value.LeaseUntil, now.Add(LeaseLifetime))
                    .SetProperty(value => value.LastAttemptAt, now)
                    .SetProperty(value => value.AttemptCount, value => value.AttemptCount + 1)
                    .SetProperty(value => value.Revision, value => value.Revision + 1), cancellationToken);
            if (claimed != 1)
                continue;
            SolutionsAuditOutboxRecord item = await db.AuditOutbox
                .AsNoTracking()
                .SingleAsync(value => value.EventId == eventId && value.LeaseId == leaseId, cancellationToken);
            try
            {
                AuditEventV1 auditEvent = ToAuditEvent(item);
                AuditEventValidationResult validation = AuditEventV1Validator.Validate(auditEvent);
                if (!validation.IsValid)
                {
                    await MarkPoisonedAsync(db, item.EventId, leaseId, validation.RejectionCode ?? "audit.envelope_invalid", cancellationToken);
                    continue;
                }
                AuditIngestionResult result = await sink.IngestAsync(auditEvent, cancellationToken);
                if (result.Disposition is AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
                {
                    await MarkPoisonedAsync(db, item.EventId, leaseId, result.RejectionCode ?? "audit.delivery_rejected", cancellationToken);
                }
                else
                {
                    AuditEventReadBackV1? readBack = await sink.ReadBackAsync(auditEvent.EventId, cancellationToken);
                    if (readBack is not null && AuditEventV1ReadBack.Matches(auditEvent, readBack))
                    {
                        await db.AuditOutbox
                            .Where(value => value.EventId == item.EventId && value.Status == "Delivering" && value.LeaseId == leaseId)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(value => value.Status, "Delivered")
                                .SetProperty(value => value.NextAttemptAt, (DateTimeOffset?)null)
                                .SetProperty(value => value.LeaseId, (Guid?)null)
                                .SetProperty(value => value.LeaseUntil, (DateTimeOffset?)null)
                                .SetProperty(value => value.LastError, (string?)null)
                                .SetProperty(value => value.DeliveredAt, clock.GetUtcNow())
                                .SetProperty(value => value.Revision, value => value.Revision + 1), cancellationToken);
                    }
                    else
                    {
                        await MarkRetryingAsync(db, item, leaseId, "audit.readback_unconfirmed", cancellationToken);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await MarkRetryingAsync(db, item, leaseId, "audit.delivery_transient", cancellationToken);
            }
        }
        return await ReadHealthAsync(db, cancellationToken);
    }

    public async Task<SolutionsAuditOutboxHealth> ReadHealthAsync(CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await ReadHealthAsync(
            scope.ServiceProvider.GetRequiredService<SolutionsDbContext>(),
            cancellationToken);
    }

    private static async Task<SolutionsAuditOutboxHealth> ReadHealthAsync(
        SolutionsDbContext db,
        CancellationToken cancellationToken) =>
        new(
            await db.AuditOutbox.CountAsync(value => value.Status == "Pending" || value.Status == "Delivering", cancellationToken),
            await db.AuditOutbox.CountAsync(value => value.Status == "Retrying", cancellationToken),
            await db.AuditOutbox.CountAsync(value => value.Status == "Delivered", cancellationToken),
            await db.AuditOutbox.CountAsync(value => value.Status == "Poisoned", cancellationToken),
            await db.AuditOutbox
                .Where(value => value.Status == "Pending" || value.Status == "Retrying" || value.Status == "Delivering")
                .MinAsync(value => (DateTimeOffset?)value.CreatedAt, cancellationToken));

    private async Task MarkRetryingAsync(
        SolutionsDbContext db,
        SolutionsAuditOutboxRecord item,
        Guid leaseId,
        string reason,
        CancellationToken cancellationToken) =>
        await db.AuditOutbox
            .Where(value => value.EventId == item.EventId && value.Status == "Delivering" && value.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, "Retrying")
                .SetProperty(value => value.NextAttemptAt, clock.GetUtcNow().Add(Backoff(item.AttemptCount)))
                .SetProperty(value => value.LeaseId, (Guid?)null)
                .SetProperty(value => value.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(value => value.LastError, Bounded(reason))
                .SetProperty(value => value.Revision, value => value.Revision + 1), cancellationToken);

    private static async Task MarkPoisonedAsync(
        SolutionsDbContext db,
        Guid eventId,
        Guid leaseId,
        string reason,
        CancellationToken cancellationToken) =>
        await db.AuditOutbox
            .Where(value => value.EventId == eventId && value.Status == "Delivering" && value.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, "Poisoned")
                .SetProperty(value => value.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(value => value.LeaseId, (Guid?)null)
                .SetProperty(value => value.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(value => value.LastError, Bounded(reason))
                .SetProperty(value => value.Revision, value => value.Revision + 1), cancellationToken);

    private static TimeSpan Backoff(int attemptCount)
    {
        double seconds = Math.Pow(2, Math.Min(Math.Max(attemptCount - 1, 0), 8));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumBackoff.TotalSeconds));
    }

    private static string Bounded(string value) => value.Length <= 200 ? value : value[..200];

    private static AuditEventV1 ToAuditEvent(SolutionsAuditOutboxRecord value) => new(
        value.EventId, value.ActorKind, value.ActorId, value.SubjectId, value.WorkspaceId, value.EventType, "solution_operation",
        value.OperationId ?? value.InstallationId ?? value.SolutionVersionId ?? value.EventId, value.Outcome, value.OccurredAt,
        value.CorrelationId, Metadata(value));

    private static IReadOnlyDictionary<string, string>? Metadata(SolutionsAuditOutboxRecord value)
    {
        Dictionary<string, string> metadata = [];
        if (value.ProblemCode is not null)
            metadata["problem_code"] = value.ProblemCode;
        if (value.OriginatingSubjectKind is SolutionSubjectKind originatingSubjectKind)
            metadata["originating_subject_kind"] = originatingSubjectKind == SolutionSubjectKind.Human ? "human" : "service";
        return metadata.Count == 0 ? null : metadata;
    }
}
