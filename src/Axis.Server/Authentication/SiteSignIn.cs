using Axis.Configuration.Model;
using Axis.Configuration.Realms;
using Axis.Data;
using Axis.Server.Applications;
using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Server.Authentication;

/// <summary>
/// Where a sign-in for a site goes: the site's path, the provider to send the person to, when there is one, and the
/// <c>acr_values</c> to ask for, when the site's providers have <c>acr</c> lists.
/// </summary>
internal sealed record SiteSignInTarget(string Path, string? ProviderHint, string? AcrValues);

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

    /// <summary>The challenge parameter that holds the <c>acr_values</c> to send. It is not saved in the sign-in state.</summary>
    public const string AcrValuesParameter = "axis:acr_values";

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

    /// <summary>The providers of <paramref name="model"/> that <paramref name="site"/> allows. Names are matched ignoring letter case.</summary>
    public static IReadOnlyList<IdentityProviderModel> AllowedProviders(ApplicationModel model, SiteModel site)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(site);

        return [.. model.IdentityProviders.Where(provider => site.SignIn.Contains(provider.Name, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// The <c>acr</c> values that the providers with <paramref name="alias"/> accept for <paramref name="site"/>: the levels of
    /// every allowed provider with that alias, joined. It is empty when no allowed provider with the alias has a list, and then
    /// the sign-in has no <c>acr</c> check.
    /// </summary>
    public static IReadOnlyList<string> AcceptedAcr(ApplicationModel model, SiteModel site, string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        return [.. AllowedProviders(model, site)
            .Where(provider => AliasOf(provider) == alias)
            .SelectMany(provider => provider.Acr)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The <c>acr_values</c> a sign-in on <paramref name="site"/> sends: the distinct levels of all its allowed providers in
    /// ordinal order, separated by spaces, or <c>null</c> when none has a list.
    /// </summary>
    public static string? AcrValues(ApplicationModel model, SiteModel site)
    {
        var levels = AllowedProviders(model, site)
            .SelectMany(provider => provider.Acr)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return levels.Count == 0 ? null : string.Join(' ', levels);
    }

    private static string? AliasOf(IdentityProviderModel provider) =>
        provider is { Type: "oidc", IssuerBinding: { } issuer } ? RealmSync.ProviderAliasPrefix + issuer.ToLowerInvariant() : null;

    /// <summary>
    /// The site that <paramref name="returnUrl"/> opens in the current tenant, or <c>null</c> when the tenant signs in through the
    /// platform client or the address opens no active site.
    /// </summary>
    public async Task<SiteSignInTarget?> FindAsync(string tenantId, string returnUrl, CancellationToken cancellationToken)
    {
        if (FirstSegment(returnUrl) is not { } segment || await FindActiveAsync(tenantId, segment, cancellationToken) is not { } found)
        {
            return null;
        }

        return new SiteSignInTarget(found.Client.Path, ProviderHint(found.Client), AcrValues(found.Active.Application, found.Active.Site));
    }

    /// <summary>
    /// Whether the active site at <paramref name="path"/> allows <paramref name="identityProvider"/>. It is <c>false</c> when
    /// the site is no longer active, or when the tenant has no site clients.
    /// </summary>
    public async Task<bool> AllowsAsync(string tenantId, string path, string? identityProvider, CancellationToken cancellationToken)
        => await SiteClientAsync(tenantId, path, cancellationToken) is { } found && Allows(found, identityProvider);

    /// <summary>
    /// The levels that the active site at <paramref name="path"/> accepts from the provider with <paramref name="alias"/>. It is
    /// empty when the provider has no list, the site is no longer active, or the tenant has no site clients.
    /// </summary>
    public async Task<IReadOnlyList<string>> AcceptedAcrAsync(string tenantId, string path, string alias, CancellationToken cancellationToken)
        => await FindActiveAsync(tenantId, path, cancellationToken) is { } found
            ? AcceptedAcr(found.Active.Application, found.Active.Site, alias)
            : [];

    private async Task<RealmSiteClient?> SiteClientAsync(string tenantId, string path, CancellationToken cancellationToken)
        => (await FindActiveAsync(tenantId, path, cancellationToken))?.Client;

    // Rebuilds the site's client from the active release with the same plan that the realm sync uses.
    private async Task<(RealmSiteClient Client, ActiveSite Active)?> FindActiveAsync(string tenantId, string path, CancellationToken cancellationToken)
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
        return plan.Plan.Sites.FirstOrDefault(site => string.Equals(site.Path, activePath, StringComparison.Ordinal)) is { } client
            ? (client, active)
            : null;
    }
}
