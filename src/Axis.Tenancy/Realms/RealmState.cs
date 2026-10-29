using Axis.Configuration.Realms;

namespace Axis.Tenancy.Realms;

/// <summary>
/// What a tenant's realm should hold: a client for each site, with the provider aliases and the accounts flag it allows, and a brokered <c>oidc</c> provider for each entry of
/// <paramref name="Providers"/>. <see cref="ActivationRealmSync"/> builds it from the realm plan and the tenant's bindings, so it holds
/// resolved values and no binding names. <paramref name="PlatformClientSecret"/> is the secret of the platform client <c>axis</c>. When it is
/// set, each site client's secret is <see cref="SiteClientSecret.Derive"/> of it. When it is <c>null</c>, Keycloak makes the secret.
/// </summary>
public sealed record RealmState(
    IReadOnlyList<RealmSiteClient> Sites,
    IReadOnlyList<RealmBrokeredProvider> Providers,
    string? PlatformClientSecret = null);

/// <summary>
/// A brokered <c>oidc</c> provider. The alias starts with <see cref="RealmSync.ProviderAliasPrefix"/>. The claims name
/// where the provider puts the person's display name, email and locale, and the claims that hold roles.
/// </summary>
public sealed record RealmBrokeredProvider(
    string Alias,
    Uri Issuer,
    string ClientId,
    string ClientSecret,
    string DisplayNameClaim,
    string EmailClaim,
    string LocaleClaim,
    IReadOnlyList<string> RolesClaims);
