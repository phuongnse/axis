using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Authorization.Infrastructure.Services;

internal sealed class AuthorizationAuditDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<AuthorizationAuditDispatcher> logger) : BackgroundService
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
                logger.LogError(exception, "Authorization audit dispatch batch failed.");
            }

            if (dispatched < BatchSize)
                await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IAuthorizationAuditDispatchStore store =
            scope.ServiceProvider.GetRequiredService<IAuthorizationAuditDispatchStore>();
        IAuditEventSink sink = scope.ServiceProvider.GetRequiredService<IAuditEventSink>();
        IReadOnlyList<AuthorizationAuditDispatchItem> items = await store.ClaimDueBatchAsync(
            clock.GetUtcNow(),
            LeaseLifetime,
            BatchSize,
            cancellationToken);

        foreach (AuthorizationAuditDispatchItem item in items)
            await DispatchAsync(store, sink, item, cancellationToken);

        return items.Count;
    }

    private async Task DispatchAsync(
        IAuthorizationAuditDispatchStore store,
        IAuditEventSink sink,
        AuthorizationAuditDispatchItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            AuditIngestionResult ingestion = await sink.IngestAsync(item.Event, cancellationToken);
            if (ingestion.Disposition is
                AuditIngestionDisposition.Conflict or AuditIngestionDisposition.Rejected)
            {
                await MarkPoisonedAsync(
                    store,
                    item,
                    ingestion.RejectionCode ?? "audit.delivery_rejected",
                    cancellationToken);
                return;
            }

            AuditEventReadBackV1? readBack = await sink.ReadBackAsync(
                item.Event.EventId,
                cancellationToken);
            if (readBack is not null && AuditEventV1ReadBack.Matches(item.Event, readBack))
            {
                bool delivered = await store.MarkDeliveredAsync(
                    item.Event.EventId,
                    item.LeaseId,
                    cancellationToken);
                if (!delivered)
                {
                    logger.LogWarning(
                        "Authorization audit delivery lease was stale for event {EventId}.",
                        item.Event.EventId);
                }

                return;
            }

            await MarkForRetryAsync(store, item, "audit.readback_unconfirmed", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Authorization audit delivery attempt failed for event {EventId}.",
                item.Event.EventId);
            await MarkForRetryAsync(store, item, "audit.delivery_transient", cancellationToken);
        }
    }

    private async Task MarkForRetryAsync(
        IAuthorizationAuditDispatchStore store,
        AuthorizationAuditDispatchItem item,
        string reason,
        CancellationToken cancellationToken)
    {
        bool updated = await store.MarkForRetryAsync(
            item.Event.EventId,
            item.LeaseId,
            clock.GetUtcNow().Add(Backoff(item.AttemptCount)),
            reason,
            cancellationToken);
        if (!updated)
        {
            logger.LogWarning(
                "Authorization audit retry lease was stale for event {EventId}.",
                item.Event.EventId);
        }
    }

    private async Task MarkPoisonedAsync(
        IAuthorizationAuditDispatchStore store,
        AuthorizationAuditDispatchItem item,
        string reason,
        CancellationToken cancellationToken)
    {
        bool updated = await store.MarkPoisonedAsync(
            item.Event.EventId,
            item.LeaseId,
            reason,
            cancellationToken);
        if (!updated)
        {
            logger.LogWarning(
                "Authorization audit poison lease was stale for event {EventId}.",
                item.Event.EventId);
        }
    }

    private static TimeSpan Backoff(int attemptCount)
    {
        double seconds = Math.Pow(2, Math.Min(Math.Max(attemptCount - 1, 0), 8));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumBackoff.TotalSeconds));
    }
}
