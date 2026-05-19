using Axis.Identity.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Api.HealthChecks;

internal sealed class PostgreSqlHealthCheck(IdentityDbContext db) : IHealthCheck
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
