using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Axis.ControlPlane.Health;

/// <summary>
/// Reports whether the control plane database accepts queries. A failure is reported with the registration's failure status,
/// so the registration decides whether it is Unhealthy or Degraded.
/// </summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The control plane database is unreachable.", exception);
        }
    }
}
