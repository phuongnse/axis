namespace Axis.Tenancy;

/// <summary>The tenants served by this installation, bound from the <c>Tenants</c> configuration section.</summary>
public sealed class TenantOptions
{
    public const string SectionName = "Tenants";

    /// <summary>Tenant settings keyed by tenant id.</summary>
    public Dictionary<string, TenantSettings> Tenants { get; } = new(StringComparer.Ordinal);
}

/// <summary>How requests reach one tenant and where its database is.</summary>
public sealed class TenantSettings
{
    /// <summary>Request host names for the tenant, matched ignoring letter case and port.</summary>
    public List<string> Hosts { get; } = [];

    public string? ConnectionString { get; set; }
}
