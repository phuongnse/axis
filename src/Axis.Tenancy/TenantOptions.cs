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

    /// <summary>
    /// The id of the main tenant when this is a preview tenant. A blank value counts as unset. A preview tenant signs
    /// people in with its main tenant's settings, without <c>EndpointAudience</c>.
    /// </summary>
    public string? PreviewOf { get; set; }

    /// <summary>The tenant's deployment bindings keyed by binding name, matched ignoring letter case.</summary>
    public Dictionary<string, DeploymentBindingSettings> Bindings { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The kind and value of one deployment binding of a tenant.</summary>
public sealed class DeploymentBindingSettings
{
    public string? Kind { get; set; }

    public string? Value { get; set; }
}

/// <summary>The kinds a deployment binding can have.</summary>
public static class DeploymentBindingKinds
{
    public const string Url = "url";
    public const string Secret = "secret";
}

/// <summary>The sign-in profiles a tenant can have.</summary>
public static class TenantSignInProfiles
{
    public const string Standard = "standard";
    public const string Fapi2 = "fapi2";
}
