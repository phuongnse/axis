namespace Axis.Tenancy.Platform;

/// <summary>A row of <c>platform.tenant_hosts</c>. A host is unique ignoring letter case across all tenants.</summary>
public sealed class TenantHostRow
{
    public string TenantId { get; set; } = "";

    public string Host { get; set; } = "";
}
