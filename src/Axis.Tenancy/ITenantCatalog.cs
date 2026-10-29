namespace Axis.Tenancy;

/// <summary>
/// The tenants this installation serves. Request resolution, tenant connections, deployment bindings, sign-in,
/// startup activation, readiness and the worker read tenants only through this contract, so none of them knows which
/// source answers it.
/// </summary>
public interface ITenantCatalog
{
    /// <summary>Every tenant, in ordinal order of id.</summary>
    IReadOnlyList<TenantEntry> Tenants { get; }

    /// <summary>Finds a tenant by id, or returns <c>null</c> when the catalog has none of that id.</summary>
    TenantEntry? Find(string tenantId);

    /// <summary>Finds the tenant for a host name without its port, ignoring letter case.</summary>
    TenantEntry? FindByHost(string host);

    /// <summary>
    /// Whether the host belongs to a tenant the source knows but does not serve, such as a suspended tenant. Such a
    /// host resolves no tenant. A source that keeps no such tenants answers <c>false</c>.
    /// </summary>
    bool IsUnavailableHost(string host);
}

/// <summary>
/// One tenant of the catalog. The connection strings are <see cref="SecretValue"/>, so the text form of an entry never
/// shows a password.
/// </summary>
/// <param name="Id">The tenant id.</param>
/// <param name="Hosts">The request host names of the tenant.</param>
/// <param name="ConnectionString">The runtime database role's connection string.</param>
/// <param name="SchemaConnectionString">The schema role's connection string, or <c>null</c> when the source has none. Only the server uses it.</param>
/// <param name="Oidc">How the tenant signs people in, or <c>null</c> when the source has none. Only the server uses it.</param>
/// <param name="Bindings">The tenant's deployment bindings keyed by name, matched ignoring letter case.</param>
/// <param name="PreviewOf">The id of the main tenant when this is a preview tenant, otherwise <c>null</c>.</param>
/// <param name="Email">The tenant's own mail settings, which replace the host's whole, or <c>null</c> when the tenant uses the host's. Both sources have them.</param>
/// <param name="Realm">What activation needs to sync the tenant's realm, or <c>null</c> when the source holds none for this tenant. Both sources have it, and a preview tenant has none.</param>
/// <param name="RealmUsers">What the server needs to list the tenant's realm users, or <c>null</c> when the source holds none for this tenant. Both sources have it, and a preview tenant has none.</param>
public sealed record TenantEntry(
    string Id,
    IReadOnlyList<string> Hosts,
    SecretValue ConnectionString,
    SecretValue? SchemaConnectionString,
    TenantOidc? Oidc,
    IReadOnlyDictionary<string, TenantBinding> Bindings,
    string? PreviewOf = null,
    EmailSettings? Email = null,
    TenantRealmAccess? Realm = null,
    TenantRealmUsers? RealmUsers = null);

/// <summary>
/// What activation needs to sync a tenant's realm: the address of the identity provider, the realm's name and the
/// secret of its <c>axis-activation</c> client, and the secret of the platform client <c>axis</c>. The secrets are <see cref="SecretValue"/>,
/// so the text form never shows them.
/// </summary>
/// <param name="BaseAddress">The identity provider's address, always ending with a slash, so a path joins onto it.</param>
/// <param name="Name">The realm's name.</param>
/// <param name="ActivationClientSecret">The secret of the <c>axis-activation</c> client.</param>
/// <param name="PlatformClientSecret">The secret of the platform client <c>axis</c>, from which the site client secrets derive (<see cref="Realms.SiteClientSecret"/>), or <c>null</c> when the tenant has none.</param>
public sealed record TenantRealmAccess(Uri BaseAddress, string Name, SecretValue ActivationClientSecret, SecretValue? PlatformClientSecret = null);

/// <summary>
/// What the server needs to list a tenant's realm users: the address of the identity provider, the realm's name and the
/// secret of its <c>axis-users</c> client. The secret is a <see cref="SecretValue"/>, so the text form never shows it.
/// </summary>
/// <param name="BaseAddress">The identity provider's address, always ending with a slash, so a path joins onto it.</param>
/// <param name="Realm">The realm's name.</param>
/// <param name="ClientSecret">The secret of the <c>axis-users</c> client.</param>
public sealed record TenantRealmUsers(Uri BaseAddress, string Realm, SecretValue ClientSecret);

/// <summary>How one tenant signs people in. A value the source leaves blank is <c>null</c>.</summary>
/// <param name="Profile">The sign-in profile as the source wrote it, or <c>null</c> when unset, which counts as <c>standard</c>. See <see cref="TenantSignInProfiles"/>.</param>
/// <param name="ClientKeys">The client keys, newest first. <c>null</c> or empty means none. A tenant holds at most two.</param>
public sealed record TenantOidc(
    string? Authority,
    string? ClientId,
    SecretValue? ClientSecret,
    string? EndpointAudience,
    string? ServiceAudience,
    string? UserIdClaim,
    string? DisplayNameClaim,
    string? RolesClaim,
    string? EmailClaim,
    string? LocaleClaim,
    string? Profile = null,
    IReadOnlyList<TenantClientKey>? ClientKeys = null);

/// <summary>
/// A client key for <c>private_key_jwt</c>: a private key as a PEM, with its key id. The key is a
/// <see cref="SecretValue"/>, so the text form never shows it.
/// </summary>
public sealed record TenantClientKey(string KeyId, SecretValue PrivateKey);

/// <summary>
/// One deployment binding. A <c>url</c> binding has <see cref="Url"/>, as written, a <c>storage</c> binding has its
/// value in <see cref="Url"/>, and a <c>secret</c> binding has <see cref="Secret"/>.
/// </summary>
public sealed record TenantBinding(string Kind, string? Url, SecretValue? Secret);
