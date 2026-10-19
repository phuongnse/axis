namespace Axis.Server.Authentication;

/// <summary>
/// The sign-in settings of the tenants served now, keyed by tenant id. The OIDC and bearer options of a tenant read their
/// settings here when they are built, so a refresh of the tenant directory swaps the settings and the next build sees them.
/// </summary>
internal sealed class TenantSignInSettings(IReadOnlyDictionary<string, TenantOidcSettings> initial)
{
    private volatile IReadOnlyDictionary<string, TenantOidcSettings> _current = initial;

    public IReadOnlyDictionary<string, TenantOidcSettings> Current => _current;

    public TenantOidcSettings? Find(string tenantId) => _current.GetValueOrDefault(tenantId);

    public void Replace(IReadOnlyDictionary<string, TenantOidcSettings> next) => _current = next;
}
