using Axis.Authorization.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Api.HealthChecks;

internal sealed class AuthorizationAuditHealthCheck(
    IAuthorizationAuditHealthReader reader,
    TimeProvider clock,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        int overdueMinutes = configuration.GetValue<int>("AuthorizationAudit:OverdueMinutes");
        if (overdueMinutes <= 0)
        {
            return HealthCheckResult.Unhealthy(
                "AuthorizationAudit:OverdueMinutes must be configured with a positive value.");
        }

        AuthorizationAuditHealthSnapshot snapshot = await reader.ReadAsync(cancellationToken);
        if (snapshot.PoisonedCount > 0)
        {
            return HealthCheckResult.Unhealthy(
                "Authorization audit delivery contains poisoned events.",
                data: new Dictionary<string, object>
                {
                    ["poisoned"] = snapshot.PoisonedCount,
                });
        }

        DateTimeOffset overdueBefore = clock.GetUtcNow().AddMinutes(-overdueMinutes);
        if (snapshot.OldestPendingAt <= overdueBefore)
        {
            return HealthCheckResult.Degraded(
                "Authorization audit delivery contains overdue pending events.",
                data: new Dictionary<string, object>
                {
                    ["oldestPendingAt"] = snapshot.OldestPendingAt.Value,
                    ["thresholdMinutes"] = overdueMinutes,
                });
        }

        return HealthCheckResult.Healthy();
    }
}
