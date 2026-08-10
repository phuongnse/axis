using Axis.Solutions.Infrastructure.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.Api.HealthChecks;

internal sealed class SolutionsAuditHealthCheck(
    SolutionsAuditDispatchWorker worker,
    TimeProvider clock,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        int overdueMinutes = configuration.GetValue<int>("SolutionsAudit:OverdueMinutes");
        if (overdueMinutes <= 0)
        {
            return HealthCheckResult.Unhealthy(
                "SolutionsAudit:OverdueMinutes must be configured with a positive value.");
        }

        SolutionsAuditOutboxHealth snapshot = await worker.ReadHealthAsync(cancellationToken);
        if (snapshot.Poisoned > 0)
        {
            return HealthCheckResult.Unhealthy(
                "Solutions audit delivery contains poisoned events.",
                data: new Dictionary<string, object>
                {
                    ["poisoned"] = snapshot.Poisoned,
                });
        }

        DateTimeOffset overdueBefore = clock.GetUtcNow().AddMinutes(-overdueMinutes);
        if (snapshot.OldestPendingAt <= overdueBefore)
        {
            return HealthCheckResult.Degraded(
                "Solutions audit delivery contains overdue pending events.",
                data: new Dictionary<string, object>
                {
                    ["oldestPendingAt"] = snapshot.OldestPendingAt.Value,
                    ["thresholdMinutes"] = overdueMinutes,
                });
        }

        return HealthCheckResult.Healthy();
    }
}
