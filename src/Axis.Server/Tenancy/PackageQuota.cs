using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Server.Tenancy;

/// <summary>
/// The package run time quota check, see <c>docs/reference/tenant-quotas.md#refusals</c>. The switch route and the Studio
/// publish routes call it, so they refuse in the same way.
/// </summary>
internal static class PackageQuota
{
    /// <summary>
    /// The quota <c>403</c> while the tenant's <c>packageCpuSecondsPerDay</c> is 0, with <c>used</c> the day's package CPU
    /// seconds, and the <c>quota.refused</c> log entry. Null otherwise, and always outside a cell, where there is no limit.
    /// </summary>
    public static async Task<IResult?> RefusalAsync(IServiceProvider services, string tenantId, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<TenantQuotas>().Limit(tenantId, QuotaKeys.PackageCpuSecondsPerDay) is not 0)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
        var used = await services.GetRequiredService<TenantDailyCounterStore>()
            .ValueAsync(tenantId, TenantDailyCounterStore.PackageCpuSeconds, today, cancellationToken);
        QuotaRefusal.Log(services.GetRequiredService<ILoggerFactory>(), tenantId, QuotaKeys.PackageCpuSecondsPerDay);
        return QuotaRefusal.Problem(QuotaKeys.PackageCpuSecondsPerDay, 0, used);
    }
}
