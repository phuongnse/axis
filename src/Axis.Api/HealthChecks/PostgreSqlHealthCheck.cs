using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Api.HealthChecks;

/// <summary>
/// Verifies PostgreSQL connectivity by attempting to open a connection via the
/// DataModeling DbContext. No extra packages needed — uses EF Core's built-in CanConnectAsync.
/// </summary>
internal sealed class PostgreSqlHealthCheck(DataModelingDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            bool canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to PostgreSQL.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", ex);
        }
    }
}
