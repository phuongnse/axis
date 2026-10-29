namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_platform_providers</c>: a brokered identity provider that the tenant's platform sign-in accepts.
/// A binding is named, never held: the row holds no secret. A provider name is unique within a tenant.
/// </summary>
public sealed class TenantPlatformProviderRow
{
    public string TenantId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><c>oidc</c> or <c>saml</c>.</summary>
    public string Type { get; set; } = "";

    public string? IssuerBinding { get; set; }

    public string? ClientIdBinding { get; set; }

    public string? ClientSecretBinding { get; set; }

    public string? ClientKeyBinding { get; set; }

    public string? MetadataBinding { get; set; }

    public string? ClaimDisplayName { get; set; }

    public string? ClaimEmail { get; set; }

    public string? ClaimLocale { get; set; }

    public string? RolesClaim { get; set; }

    /// <summary>A <c>jsonb</c> object that maps a value of the roles claim to a platform role. <c>{}</c> when there is none.</summary>
    public string RoleValues { get; set; } = "{}";
}
