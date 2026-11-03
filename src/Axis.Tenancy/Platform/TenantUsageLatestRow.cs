namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_usage_latest</c>: the newest storage sample of one tenant. It holds sizes only.
/// </summary>
public sealed class TenantUsageLatestRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The bytes of the tenant database without its stored file contents.</summary>
    public long DatabaseBytes { get; set; }

    /// <summary>The on-disk bytes of the tenant's stored file contents.</summary>
    public long FileBytes { get; set; }

    public DateTimeOffset SampledAt { get; set; }
}
