using Axis.Tenancy;

namespace Axis.Server.Studio;

/// <summary>
/// Finds the preview tenant of a main tenant. The catalog lists only the tenants the server serves, so a preview
/// tenant that is suspended or has no host entry is not found, and a tenant with no preview tenant answers <c>null</c>.
/// </summary>
internal static class StudioPreview
{
    /// <summary>The served tenant whose <c>PreviewOf</c> is <paramref name="tenantId"/>, or <c>null</c>.</summary>
    public static TenantEntry? Find(ITenantCatalog catalog, string tenantId) =>
        catalog.Tenants.FirstOrDefault(entry => string.Equals(entry.PreviewOf, tenantId, StringComparison.Ordinal));
}
