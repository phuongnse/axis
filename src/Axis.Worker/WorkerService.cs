using Axis.Processes.Work;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// Polls every configured tenant in ordinal order of its id and runs its due work items until none
/// is left, then waits for the poll interval when no tenant had work. A tenant whose database the
/// server has not migrated yet is skipped until it has.
/// </summary>
internal sealed partial class WorkerService(
    TenantOptions tenants,
    WorkerOptions options,
    WorkItemRunner runner,
    ILogger<WorkerService> logger) : BackgroundService
{
    private readonly string[] _tenantIds = [.. tenants.Tenants.Keys.Order(StringComparer.Ordinal)];

    // Tenants whose database was migrated. A new schema arrives with a new release, which restarts
    // the worker, so a ready tenant is not checked again.
    private readonly HashSet<string> _ready = new(StringComparer.Ordinal);
    private readonly HashSet<string> _waitingLogged = new(StringComparer.Ordinal);

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
            if (!_ready.Contains(tenantId))
            {
                if (!await runner.IsMigratedAsync(tenantId, stoppingToken))
                {
                    if (_waitingLogged.Add(tenantId))
                    {
                        LogWaitingForMigrations(tenantId);
                    }

                    return false;
                }

                _ready.Add(tenantId);
                LogReady(tenantId);
            }

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

    [LoggerMessage(LogLevel.Information, "Waiting for the database of tenant {TenantId} to be migrated.")]
    private partial void LogWaitingForMigrations(string tenantId);

    [LoggerMessage(LogLevel.Information, "Tenant {TenantId} is ready for work.")]
    private partial void LogReady(string tenantId);

    [LoggerMessage(LogLevel.Error, "Running work items for tenant {TenantId} failed.")]
    private partial void LogRunFailed(Exception exception, string tenantId);
}
