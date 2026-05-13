using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Axis.Api.HealthChecks;

/// <summary>
/// Verifies Redis connectivity by issuing a PING via the shared IConnectionMultiplexer.
/// Uses StackExchange.Redis already registered in DI — no extra packages needed.
/// </summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed.", ex);
        }
    }
}
