using Axis.Server.Tenancy;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Server.Health;

/// <summary>
/// Reports Degraded while the last read of the tenant directory failed. The server keeps serving the last good read,
/// so it stays in service. The description names no tenant.
/// </summary>
internal sealed class TenantDirectoryHealthCheck(TenantDirectoryRefreshStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(status.Failing ? HealthCheckResult.Degraded("The tenant directory could not be read again.") : HealthCheckResult.Healthy());
}
