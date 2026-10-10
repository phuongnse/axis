using Axis.Processes.Work;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// Polls every configured tenant in ordinal order of its id and runs its due work items until none
/// is left, then waits for the poll interval when no tenant had work.
/// </summary>
internal sealed partial class WorkerService(
    TenantOptions tenants,
    WorkerOptions options,
    WorkItemRunner runner,
    ILogger<WorkerService> logger) : BackgroundService
{
    private readonly string[] _tenantIds = [.. tenants.Tenants.Keys.Order(StringComparer.Ordinal)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var ranAny = false;
            foreach (var tenantId in _tenantIds)
            {
                using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
                {
                    ranAny |= await RunTenantAsync(tenantId, stoppingToken);
                }
            }

            if (!ranAny)
            {
                await Task.Delay(options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> RunTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        var ran = false;
        try
        {
            while (await runner.RunNextAsync(tenantId, options.LeaseDuration, stoppingToken))
            {
                ran = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // A claimed item reappears when its lease expires. The tenant is polled again in the next
            // round, after the poll interval, so a database that is down is not polled in a tight loop.
            LogRunFailed(exception, tenantId);
            return false;
        }

        return ran;
    }

    [LoggerMessage(LogLevel.Error, "Running work items for tenant {TenantId} failed.")]
    private partial void LogRunFailed(Exception exception, string tenantId);
}
