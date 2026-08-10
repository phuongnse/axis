using Axis.Solutions.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Solutions.Infrastructure.Services;

public sealed class SolutionsBackgroundService(
    SolutionOperationWorker operations,
    TimeProvider clock,
    ILogger<SolutionsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<Guid> runnable = await operations.ListRunnableAsync(
                    clock.GetUtcNow(),
                    maximumCount: 10,
                    stoppingToken);
                foreach (Guid operationId in runnable)
                {
                    try
                    {
                        await operations.ProcessAsync(
                            operationId,
                            LeaseDuration,
                            stoppingToken);
                    }
                    catch (Exception exception) when (exception is
                        SolutionPersistenceException or
                        SolutionPackageException or
                        SolutionAdapterException or
                        InvalidOperationException)
                    {
                        logger.LogWarning(
                            exception,
                            "Solution operation {OperationId} did not advance.",
                            operationId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Solutions background processing failed.");
            }

            await Task.Delay(IdleDelay, clock, stoppingToken);
        }
    }
}
