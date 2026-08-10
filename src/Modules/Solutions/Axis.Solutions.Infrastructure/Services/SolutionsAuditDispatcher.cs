using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Solutions.Infrastructure.Services;

internal sealed class SolutionsAuditDispatcher(
    SolutionsAuditDispatchWorker worker,
    TimeProvider clock,
    ILogger<SolutionsAuditDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await worker.DispatchAsync(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Solutions audit dispatch batch failed.");
            }

            await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }
}
