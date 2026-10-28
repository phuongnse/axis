using Axis.Configuration.Model;

namespace Axis.Configuration.Realms;

/// <summary>
/// The state of the tenant's realm that activation wants: a client for each site and a brokered provider for each
/// set of <c>oidc</c> bindings. Nothing applies it to the realm yet.
/// </summary>
/// <param name="Sites">The site clients, in ordinal path order.</param>
/// <param name="Providers">The providers, in ordinal alias order.</param>
public sealed record RealmPlan(IReadOnlyList<RealmSiteClient> Sites, IReadOnlyList<RealmProvider> Providers);

/// <summary>The client of one site: the providers it allows, and whether it allows the <c>accounts</c> provider.</summary>
/// <param name="Path">The site path.</param>
/// <param name="Accounts">Whether an allowed provider is of type <c>accounts</c>.</param>
/// <param name="Providers">The aliases of the allowed providers that the plan holds, in ordinal order.</param>
public sealed record RealmSiteClient(string Path, bool Accounts, IReadOnlyList<string> Providers);

/// <summary>
/// One brokered provider of the realm. Applications whose providers name the same bindings, ignoring letter case,
/// share it.
/// </summary>
/// <param name="Alias">The stable alias: <c>oidc-</c> and the lower-case issuer binding name.</param>
/// <param name="RolesClaims">The distinct roles claims of the providers that share it, in ordinal order.</param>
public sealed record RealmProvider(
    string Alias,
    string IssuerBinding,
    string ClientIdBinding,
    string? ClientSecretBinding,
    string? ClientKeyBinding,
    IdentityProviderClaimsModel Claims,
    IReadOnlyList<string> RolesClaims);
