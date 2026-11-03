namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_daily_counters</c>: one named count of one tenant on one UTC day, such as the emails a
/// cell's worker sent. It holds a count only, so it never holds a record value, an address or a user name.
/// </summary>
public sealed class TenantDailyCounterRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The UTC day.</summary>
    public DateOnly Day { get; set; }

    /// <summary>What the row counts, such as <see cref="TenantDailyCounterStore.Emails"/>.</summary>
    public string Counter { get; set; } = "";

    public long Value { get; set; }
}
