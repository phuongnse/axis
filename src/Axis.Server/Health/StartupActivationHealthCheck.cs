using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Server.Health;

/// <summary>Reports whether startup activation has finished for every tenant.</summary>
internal sealed class StartupActivationHealthCheck(StartupActivationStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(status.IsFinished
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Startup activation has not finished."));
}
