using Axis.Identity.Application.Commands.MarkWorkspaceTransitionRedisCleanupCompleted;
using Axis.Identity.Application.Queries.ListWorkspaceTransitionCleanupItems;
using MediatR;

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
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await cleanupBatch.ExecuteAsync(sender, BatchSize, ct);
    }
}

internal sealed class WorkspaceTransitionCleanupBatch(
    IWorkspaceTransitionTicketCleanup tickets,
    TimeProvider clock)
{
    public async Task<int> ExecuteAsync(
        ISender sender,
        int batchSize,
        CancellationToken ct)
    {
        IReadOnlyList<WorkspaceTransitionCleanupItemDto> items = await sender.Send(
            new ListWorkspaceTransitionCleanupItemsQuery(batchSize),
            ct);
        foreach (WorkspaceTransitionCleanupItemDto item in items)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (item.IsCompleted)
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
            await sender.Send(
                new MarkWorkspaceTransitionRedisCleanupCompletedCommand(item.TransitionId, now),
                ct);
        }

        return items.Count;
    }
}
