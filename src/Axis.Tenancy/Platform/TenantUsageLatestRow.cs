namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_usage_latest</c>: the newest storage sample of one tenant and the counts of its failed work from the same pass. It holds sizes and counts only.
/// </summary>
public sealed class TenantUsageLatestRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The bytes of the tenant database without its stored file contents.</summary>
    public long DatabaseBytes { get; set; }

    /// <summary>The on-disk bytes of the tenant's stored file contents.</summary>
    public long FileBytes { get; set; }

    /// <summary>The number of failed instances, or <c>null</c> for a row written before the counts existed.</summary>
    public long? FailedInstances { get; set; }

    /// <summary>The number of connector and package calls whose outcome is unknown, or <c>null</c> for a row written before the counts existed.</summary>
    public long? UnknownCalls { get; set; }

    /// <summary>The number of dead letters in the <c>open</c> state, or <c>null</c> for a row written before the counts existed.</summary>
    public long? OpenDeadLetters { get; set; }

    public DateTimeOffset SampledAt { get; set; }
}
