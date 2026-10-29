using Axis.Configuration.Realms;
using Axis.Data;
using Axis.Server.Applications;
using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Server.Authentication;

/// <summary>Where a sign-in for a site goes: the site's path, and the provider to send the person to, when there is one.</summary>
internal sealed record SiteSignInTarget(string Path, string? ProviderHint);

/// <summary>
/// Finds out whether a sign-in starts on a site page, so it goes to the realm through the site's own client
/// (<c>site-&lt;path&gt;</c>). A site that allows exactly one brokered provider and no accounts also names that provider, so
/// the realm skips its own page. A preview tenant, a <c>fapi2</c> tenant and a tenant without realm details have no
/// <see cref="TenantOidcSettings.SiteClientKey"/>, so they keep the platform client.
/// </summary>
internal sealed class SiteSignIn(ITenantCatalog catalog, TenantSignInSettings settings, ActiveApplicationResolver resolver)
{
    /// <summary>The sign-in state item that holds the path of the site that started the sign-in.</summary>
    public const string SitePathItem = "axis:sign_in_site";

    /// <summary>The challenge parameter that holds the alias of the provider to go to. It is not saved in the sign-in state.</summary>
    public const string ProviderHintParameter = "axis:kc_idp_hint";

    public static string ClientId(string path) => RealmSync.SiteClientId(path);

    /// <summary>The text after the leading <c>/</c> up to the next <c>/</c>, <c>?</c> or <c>#</c>, or <c>null</c> when it is empty.</summary>
    public static string? FirstSegment(string returnUrl)
    {
        ArgumentNullException.ThrowIfNull(returnUrl);

        var rest = returnUrl.StartsWith('/') ? returnUrl[1..] : returnUrl;
        var end = rest.AsSpan().IndexOfAny('/', '?', '#');
        var segment = end < 0 ? rest : rest[..end];
        return segment.Length == 0 ? null : segment;
    }

    /// <summary>The alias of the only provider of a site that allows no accounts, or <c>null</c> in every other case.</summary>
    public static string? ProviderHint(RealmSiteClient site)
    {
        ArgumentNullException.ThrowIfNull(site);

        return site is { Accounts: false, Providers: [var alias] } ? alias : null;
    }

    /// <summary>
    /// Whether <paramref name="site"/> allows the provider that signed the person in. A token without the
    /// <c>identity_provider</c> claim comes from the <c>accounts</c> provider. Aliases are compared exactly.
    /// </summary>
    public static bool Allows(RealmSiteClient site, string? identityProvider)
    {
        ArgumentNullException.ThrowIfNull(site);

        return string.IsNullOrWhiteSpace(identityProvider)
            ? site.Accounts
            : site.Providers.Contains(identityProvider, StringComparer.Ordinal);
    }

    /// <summary>
    /// The site that <paramref name="returnUrl"/> opens in the current tenant, or <c>null</c> when the tenant signs in through the
    /// platform client or the address opens no active site.
    /// </summary>
    public async Task<SiteSignInTarget?> FindAsync(string tenantId, string returnUrl, CancellationToken cancellationToken)
    {
        if (FirstSegment(returnUrl) is not { } segment || await SiteClientAsync(tenantId, segment, cancellationToken) is not { } found)
        {
            return null;
        }

        return new SiteSignInTarget(found.Path, ProviderHint(found));
    }

    /// <summary>
    /// Whether the active site at <paramref name="path"/> allows <paramref name="identityProvider"/>. It is <c>false</c> when
    /// the site is no longer active, or when the tenant has no site clients.
    /// </summary>
    public async Task<bool> AllowsAsync(string tenantId, string path, string? identityProvider, CancellationToken cancellationToken)
        => await SiteClientAsync(tenantId, path, cancellationToken) is { } found && Allows(found, identityProvider);

    // Rebuilds the site's client from the active release with the same plan that the realm sync uses.
    private async Task<RealmSiteClient?> SiteClientAsync(string tenantId, string path, CancellationToken cancellationToken)
    {
        if (settings.Find(tenantId)?.SiteClientKey is null
            || await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
        {
            return null;
        }

        var activePath = active.Site.Path;
        var plan = RealmBindingFilter.Plan(
            (await resolver.ListAsync(cancellationToken)).Select(RealmApplication.FromModel),
            TenantActivationBindings.For(catalog.Find(tenantId)!, allowHttp: false));
        return plan.Plan.Sites.FirstOrDefault(site => string.Equals(site.Path, activePath, StringComparison.Ordinal));
    }
}
