namespace Axis.Tenancy;

/// <summary>
/// The operator's billing page, outside Axis, see <c>docs/reference/host-settings.md#cell-settings</c>. The cell
/// (<c>Cell:BillingPageUrl</c>) and the control plane (<c>ControlPlane:BillingPageUrl</c>) set its address. The address may hold
/// <c>{tenantId}</c>, which is replaced by the tenant's id escaped for a URL.
/// </summary>
public static class BillingPage
{
    private const string TenantIdPlaceholder = "{tenantId}";

    /// <summary>The address for a tenant, or <c>null</c> when <paramref name="address"/> is blank.</summary>
    public static string? Of(string? address, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        return string.IsNullOrWhiteSpace(address)
            ? null
            : address.Trim().Replace(TenantIdPlaceholder, Uri.EscapeDataString(tenantId), StringComparison.Ordinal);
    }

    /// <summary>True when the address, with a tenant id in place, is an absolute https URL.</summary>
    public static bool IsValid(string address) =>
        Of(address, "tenant") is { } filled
        && Uri.TryCreate(filled, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
