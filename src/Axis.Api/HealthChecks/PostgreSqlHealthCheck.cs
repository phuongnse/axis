using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Axis.Api.HealthChecks;

internal sealed class PostgreSqlHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await using NpgsqlConnection conn = dataSource.CreateConnection();
            await conn.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", ex);
        }
    }
}
