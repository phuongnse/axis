using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;

namespace Axis.Worker;

/// <summary>
/// Counts the emails of a cell's worker in the tenant's <c>emails</c> counter of the UTC day, against the tenant's
/// <c>emailsPerDay</c> quota. A tenant with no limit is counted too, so the count stays right if a limit is set later.
/// The worker's clock decides the day, as it does for <see cref="CellWorkItemUsage"/>.
/// </summary>
public sealed class CellEmailQuota(TenantDailyCounterStore counters, TenantQuotas quotas, TimeProvider time) : IEmailQuota
{
    public async Task<EmailSlot> TakeAsync(string tenantId, CancellationToken cancellationToken)
    {
        var day = Today();
        var taken = await counters.TryIncrementAsync(tenantId, TenantDailyCounterStore.Emails, day, quotas.Limit(tenantId, QuotaKeys.EmailsPerDay), cancellationToken);
        return new EmailSlot(day, taken);
    }

    public Task ReturnAsync(string tenantId, DateOnly day, CancellationToken cancellationToken) =>
        counters.DecrementAsync(tenantId, TenantDailyCounterStore.Emails, day, cancellationToken);

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
