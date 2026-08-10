using Axis.Identity.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Api.HealthChecks;

internal sealed class IdentityAuditHealthCheck(
    IIdentityAuditHealthReader reader,
    TimeProvider clock,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        int overdueMinutes = configuration.GetValue<int>("IdentityAudit:OverdueMinutes");
        if (overdueMinutes <= 0)
        {
            return HealthCheckResult.Unhealthy(
                "IdentityAudit:OverdueMinutes must be configured with a positive value.");
        }

        IdentityAuditHealthSnapshot snapshot = await reader.ReadAsync(cancellationToken);
        if (snapshot.PoisonedCount > 0)
        {
            return HealthCheckResult.Unhealthy(
                "Identity audit delivery contains poisoned events.",
                data: new Dictionary<string, object>
                {
                    ["poisoned"] = snapshot.PoisonedCount,
                });
        }

        DateTimeOffset overdueBefore = clock.GetUtcNow().AddMinutes(-overdueMinutes);
        if (snapshot.OldestPendingAt <= overdueBefore)
        {
            return HealthCheckResult.Degraded(
                "Identity audit delivery contains overdue pending events.",
                data: new Dictionary<string, object>
                {
                    ["oldestPendingAt"] = snapshot.OldestPendingAt.Value,
                    ["thresholdMinutes"] = overdueMinutes,
                });
        }

        return HealthCheckResult.Healthy();
    }
}
