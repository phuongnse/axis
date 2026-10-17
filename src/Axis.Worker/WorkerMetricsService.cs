namespace Axis.Worker;

/// <summary>
/// Refreshes the due work item and pending computed field gauges once at startup and then every
/// <see cref="WorkerOptions.MetricsInterval"/>. It runs apart from <see cref="WorkerService"/>, so an
/// item that hangs does not stop it, and it does not slow the work loop.
/// </summary>
internal sealed class WorkerMetricsService(WorkerOptions options, WorkerGauges gauges, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.MetricsInterval, timeProvider);
        do
        {
            await gauges.RefreshAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
