namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_site_legal_links</c>: the privacy and terms addresses of one site of a tenant. The site path is
/// lower case, and the site does not need to exist. At least one address is set.
/// </summary>
public sealed class TenantSiteLegalRow
{
    public string TenantId { get; set; } = "";

    public string SitePath { get; set; } = "";

    /// <summary>The site's privacy address, an absolute <c>https</c> address, or <c>null</c> for none.</summary>
    public string? PrivacyUrl { get; set; }

    /// <summary>The site's terms address, an absolute <c>https</c> address, or <c>null</c> for none.</summary>
    public string? TermsUrl { get; set; }
}
