using Axis.Identity.Application.Commands.ExpireWorkspaceContextTransition;
using Axis.Identity.Application.Queries.ListExpiredWorkspaceContextTransitions;
using MediatR;

namespace Axis.Api.Infrastructure;

internal sealed class WorkspaceTransitionExpiryService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WorkspaceTransitionExpiryService> logger) : BackgroundService
{
    private const int BatchSize = 32;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workspace transition expiry batch failed.");
            }

            await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }

    internal async Task<int> ExpireBatchAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISender mediator = scope.ServiceProvider.GetRequiredService<ISender>();
        IReadOnlyList<ExpiredWorkspaceContextTransitionDto> items = await mediator.Send(
            new ListExpiredWorkspaceContextTransitionsQuery(clock.GetUtcNow(), BatchSize),
            ct);
        foreach (ExpiredWorkspaceContextTransitionDto item in items)
        {
            await mediator.Send(
                new ExpireWorkspaceContextTransitionCommand(
                    item.TransitionId,
                    item.UserId,
                    item.SourceCorrelationDigest),
                ct);
        }

        return items.Count;
    }
}
