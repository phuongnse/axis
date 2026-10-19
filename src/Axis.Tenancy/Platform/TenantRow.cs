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

    public string? ClaimUserId { get; set; }

    public string? ClaimDisplayName { get; set; }

    public string? ClaimRoles { get; set; }

    public string? EndpointAudience { get; set; }

    /// <summary>The newest migration applied to the tenant database. Empty until the first migration.</summary>
    public string SchemaVersion { get; set; } = "";
}
