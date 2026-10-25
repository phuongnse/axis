namespace Axis.Tenancy.Platform;

/// <summary>A row of <c>platform.tenants</c>. Each secret is stored encrypted.</summary>
public sealed class TenantRow
{
    public string Id { get; set; } = "";

    /// <summary><c>provisioning</c>, <c>active</c> or <c>suspended</c>.</summary>
    public string Status { get; set; } = "";

    public long Version { get; set; } = 1;

    public string DatabaseHost { get; set; } = "";

    public int DatabasePort { get; set; }

    public string DatabaseName { get; set; } = "";

    public string RuntimeUser { get; set; } = "";

    public string SchemaUser { get; set; } = "";

    public byte[] RuntimePassword { get; set; } = [];

    public byte[] SchemaPassword { get; set; } = [];

    public string OidcAuthority { get; set; } = "";

    public string OidcClientId { get; set; } = "";

    public byte[] OidcClientSecret { get; set; } = [];

    /// <summary><c>standard</c> or <c>fapi2</c>. See <see cref="TenantSignInProfiles"/>.</summary>
    public string OidcProfile { get; set; } = TenantSignInProfiles.Standard;

    /// <summary>The id of the current client key, the newest one. <c>null</c> when the tenant has no client key.</summary>
    public string? OidcClientKeyId { get; set; }

    /// <summary>The current client key as a PEM, stored encrypted. <c>null</c> when the tenant has no client key.</summary>
    public byte[]? OidcClientKey { get; set; }

    /// <summary>The id of the previous client key, kept beside the current one during a rotation.</summary>
    public string? OidcPreviousClientKeyId { get; set; }

    /// <summary>The previous client key as a PEM, stored encrypted. Only a tenant with a current key has one.</summary>
    public byte[]? OidcPreviousClientKey { get; set; }

    public string? ClaimUserId { get; set; }

    public string? ClaimDisplayName { get; set; }

    public string? ClaimRoles { get; set; }

    public string? ClaimEmail { get; set; }

    public string? ClaimLocale { get; set; }

    public string? EndpointAudience { get; set; }

    public string? ServiceAudience { get; set; }

    /// <summary>
    /// The id of the main tenant when this is a preview tenant, otherwise <c>null</c>. A tenant has at most one preview
    /// tenant. A preview tenant has no sign-in settings of its own, and the catalog takes those of its main tenant.
    /// </summary>
    public string? PreviewOf { get; set; }

    /// <summary>The newest migration applied to the tenant database. Empty until the first migration.</summary>
    public string SchemaVersion { get; set; } = "";
}
