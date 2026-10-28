namespace Axis.Tenancy.Realms;

/// <summary>
/// What a tenant's realm should hold: a client for each site path and a brokered <c>oidc</c> provider for each entry of
/// <paramref name="Providers"/>. A later part builds it from the realm plan and the tenant's bindings, so it holds
/// resolved values and no binding names.
/// </summary>
public sealed record RealmState(IReadOnlyList<string> SitePaths, IReadOnlyList<RealmBrokeredProvider> Providers);

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
