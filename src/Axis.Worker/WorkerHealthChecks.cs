using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Worker;

/// <summary>Unhealthy when the loop has not polled within twice the lease. Never Degraded.</summary>
internal sealed partial class WorkerLoopHealthCheck(WorkerHealth health, WorkerOptions options, ILogger<WorkerLoopHealthCheck> logger) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!health.LoopStalled)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        LogLoopStalled(options.LeaseDuration * 2);
        return Task.FromResult(HealthCheckResult.Unhealthy());
    }

    [LoggerMessage(LogLevel.Warning, "The worker loop has not polled within {StallTime}.")]
    private partial void LogLoopStalled(TimeSpan stallTime);
}

/// <summary>Unhealthy until the worker has seen the database of every tenant migrated. Never Degraded.</summary>
internal sealed class TenantMigrationsHealthCheck(WorkerHealth health) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(health.AllTenantsMigrated ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}

/// <summary>Unhealthy until the worker has read the tenant directory once. Never Degraded.</summary>
internal sealed class TenantDirectoryHealthCheck(WorkerTenantCatalog catalog) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(catalog.IsLoaded ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}
