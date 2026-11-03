namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_leases</c>: one running unit of work that counts against a tenant's lease quota, such as
/// <c>concurrentQueries</c>. A lease whose <see cref="ExpiresAt"/> has passed does not count, so work that died frees its slot.
/// </summary>
public sealed class TenantLeaseRow
{
    public Guid LeaseId { get; set; }

    public string TenantId { get; set; } = "";

    /// <summary>The key of the quota that the lease counts against, such as <c>concurrentQueries</c>.</summary>
    public string Quota { get; set; } = "";

    /// <summary>The host and process that run the work.</summary>
    public string Holder { get; set; } = "";

    /// <summary>When the lease ends if nothing renews it, by the database clock.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
