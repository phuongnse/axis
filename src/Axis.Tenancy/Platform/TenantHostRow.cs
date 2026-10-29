namespace Axis.Tenancy.Platform;

/// <summary>A row of <c>platform.tenant_hosts</c>. A host is unique ignoring letter case across all tenants.</summary>
public sealed class TenantHostRow
{
    public string TenantId { get; set; } = "";

    public string Host { get; set; } = "";

    /// <summary>The path of the site this host belongs to, in lower case, or null for a host that belongs to no site. A site has at most one host per tenant.</summary>
    public string? SitePath { get; set; }
}
