using Axis.Configuration.Realms;

namespace Axis.Tenancy.Realms;

/// <summary>
/// What a tenant's realm should hold: a client for each site, with the provider aliases and the accounts flag it allows, and a brokered <c>oidc</c> or <c>saml</c> provider for each entry of
/// <paramref name="Providers"/>. <see cref="ActivationRealmSync"/> builds it from the realm plan and the tenant's bindings, so it holds
/// resolved values and no binding names. <paramref name="PlatformClientSecret"/> is the secret of the platform client <c>axis</c>. When it is
/// set, each site client's secret is <see cref="SiteClientSecret.Derive"/> of it. When it is <c>null</c>, Keycloak makes the secret.
/// </summary>
public sealed record RealmState(
    IReadOnlyList<RealmSiteClient> Sites,
    IReadOnlyList<RealmBrokeredProvider> Providers,
    string? PlatformClientSecret = null);

/// <summary>
/// A brokered provider. An <c>oidc</c> provider has an issuer, a client id and a client secret, and its alias starts with
/// <see cref="RealmSync.ProviderAliasPrefix"/>. A <c>saml</c> provider has the address of its <paramref name="Metadata"/>
/// instead, and its alias starts with <see cref="RealmSync.SamlProviderAliasPrefix"/>. The claims name
/// where the provider puts the person's display name, email and locale, and the claims that hold roles. For a <c>saml</c> provider they are attribute names.
/// <paramref name="Labels"/> holds the text of the "Continue with" label for each locale. When it is empty or null, the
/// provider has no label and the sign-in page shows its alias.
/// </summary>
public sealed record RealmBrokeredProvider(
    string Alias,
    Uri? Issuer,
    string? ClientId,
    string? ClientSecret,
    string DisplayNameClaim,
    string EmailClaim,
    string LocaleClaim,
    IReadOnlyList<string> RolesClaims,
    IReadOnlyDictionary<string, string>? Labels = null,
    Uri? Metadata = null);
