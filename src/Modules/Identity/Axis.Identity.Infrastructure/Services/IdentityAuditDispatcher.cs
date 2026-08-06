using Axis.Audit.Contracts;
using Axis.Identity.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class IdentityAuditDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<IdentityAuditDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 32;
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int dispatched = 0;
            try
            {
                dispatched = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Identity audit dispatch batch failed.");
            }

            if (dispatched < BatchSize)
                await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IIdentityAuditDispatchStore store =
            scope.ServiceProvider.GetRequiredService<IIdentityAuditDispatchStore>();
        IAuditEventSink sink = scope.ServiceProvider.GetRequiredService<IAuditEventSink>();
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<IdentityAuditDispatchItem> items = await store.ClaimDueBatchAsync(
            now,
            LeaseLifetime,
            BatchSize,
            ct);

        foreach (IdentityAuditDispatchItem item in items)
            await DispatchAsync(store, sink, item, ct);

        return items.Count;
    }

    private async Task DispatchAsync(
        IIdentityAuditDispatchStore store,
        IAuditEventSink sink,
        IdentityAuditDispatchItem item,
        CancellationToken ct)
    {
        try
        {
            AuditIngestionResult ingestion = await sink.IngestAsync(item.Event, ct);
            if (ingestion.Disposition is
                AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
            {
                await MarkPoisonedAsync(
                    store,
                    item,
                    ingestion.RejectionCode ?? "audit.delivery_rejected",
                    ct);
                return;
            }

            AuditEventReadBackV1? readBack = await sink.ReadBackAsync(item.Event.EventId, ct);
            if (readBack is not null && AuditEventV1ReadBack.Matches(item.Event, readBack))
            {
                bool delivered = await store.MarkDeliveredAsync(
                    item.Event.EventId,
                    item.LeaseId,
                    clock.GetUtcNow(),
                    ct);
                if (!delivered)
                {
                    logger.LogWarning(
                        "Identity audit delivery lease was stale for event {EventId}.",
                        item.Event.EventId);
                }

                return;
            }

            await MarkForRetryAsync(store, item, "audit.readback_unconfirmed", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Identity audit delivery attempt failed for event {EventId}.",
                item.Event.EventId);
            await MarkForRetryAsync(store, item, "audit.delivery_transient", ct);
        }
    }

    private async Task MarkForRetryAsync(
        IIdentityAuditDispatchStore store,
        IdentityAuditDispatchItem item,
        string reason,
        CancellationToken ct)
    {
        TimeSpan backoff = Backoff(item.AttemptCount);
        bool updated = await store.MarkForRetryAsync(
            item.Event.EventId,
            item.LeaseId,
            clock.GetUtcNow().Add(backoff),
            reason,
            ct);
        if (!updated)
        {
            logger.LogWarning(
                "Identity audit retry lease was stale for event {EventId}.",
                item.Event.EventId);
        }
    }

    private async Task MarkPoisonedAsync(
        IIdentityAuditDispatchStore store,
        IdentityAuditDispatchItem item,
        string reason,
        CancellationToken ct)
    {
        bool updated = await store.MarkPoisonedAsync(
            item.Event.EventId,
            item.LeaseId,
            reason,
            ct);
        if (!updated)
        {
            logger.LogWarning(
                "Identity audit poison lease was stale for event {EventId}.",
                item.Event.EventId);
        }
    }

    private static TimeSpan Backoff(int attemptCount)
    {
        double seconds = Math.Pow(2, Math.Min(Math.Max(attemptCount - 1, 0), 8));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumBackoff.TotalSeconds));
    }
}
