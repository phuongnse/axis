using Axis.Configuration.Model;

namespace Axis.Configuration.Realms;

/// <summary>
/// The state of the tenant's realm that activation wants: a client for each site and a brokered provider for each
/// set of <c>oidc</c> or <c>saml</c> bindings. Startup activation applies it to the realm through <c>ActivationRealmSync</c>.
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
/// <param name="Alias">
/// The stable alias: <c>oidc-</c> and the lower-case issuer binding name, or <c>saml-</c> and the lower-case metadata
/// binding name.
/// </param>
/// <param name="IssuerBinding">The issuer binding of an <c>oidc</c> provider, <see langword="null"/> for <c>saml</c>.</param>
/// <param name="ClientIdBinding">The client id binding of an <c>oidc</c> provider, <see langword="null"/> for <c>saml</c>.</param>
/// <param name="RolesClaims">The distinct roles claims of the providers that share it, in ordinal order.</param>
/// <param name="Labels">
/// The text of the provider's "Continue with" label for each platform locale, from the texts of the first application
/// that uses the provider. Every platform locale has a label.
/// </param>
/// <param name="Type">The provider type: <c>oidc</c> or <c>saml</c>.</param>
/// <param name="MetadataBinding">The metadata binding of a <c>saml</c> provider, <see langword="null"/> for <c>oidc</c>.</param>
public sealed record RealmProvider(
    string Alias,
    string? IssuerBinding,
    string? ClientIdBinding,
    string? ClientSecretBinding,
    string? ClientKeyBinding,
    IdentityProviderClaimsModel Claims,
    IReadOnlyList<string> RolesClaims,
    IReadOnlyDictionary<string, string> Labels,
    string Type = "oidc",
    string? MetadataBinding = null);
