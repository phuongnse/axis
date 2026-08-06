using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Api.Infrastructure;

internal sealed class WorkspaceTransitionCleanupService(
    IServiceScopeFactory scopeFactory,
    WorkspaceTransitionCleanupBatch cleanupBatch,
    TimeProvider clock,
    ILogger<WorkspaceTransitionCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 32;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workspace transition Redis cleanup batch failed.");
            }

            await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }

    internal async Task<int> CleanupBatchAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IWorkspaceTransitionCleanupStore store =
            scope.ServiceProvider.GetRequiredService<IWorkspaceTransitionCleanupStore>();
        return await cleanupBatch.ExecuteAsync(store, BatchSize, ct);
    }
}

internal sealed class WorkspaceTransitionCleanupBatch(
    IWorkspaceTransitionTicketCleanup tickets,
    TimeProvider clock)
{
    public async Task<int> ExecuteAsync(
        IWorkspaceTransitionCleanupStore store,
        int batchSize,
        CancellationToken ct)
    {
        IReadOnlyList<WorkspaceTransitionCleanupItem> items =
            await store.ListTerminalWithoutRedisCleanupAsync(batchSize, ct);
        foreach (WorkspaceTransitionCleanupItem item in items)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (item.Status == WorkspaceContextTransitionStatus.Completed)
            {
                await tickets.RemoveByCorrelationDigestAsync(
                    item.SourceCorrelationDigest,
                    transition: false,
                    ct);
                if (now < item.ExpiresAt)
                    continue;
            }
            await tickets.RemoveByCorrelationDigestAsync(
                item.TargetCorrelationDigest,
                transition: true,
                ct);
            await store.MarkRedisCleanupCompletedAsync(item.TransitionId, now, ct);
        }

        return items.Count;
    }
}
