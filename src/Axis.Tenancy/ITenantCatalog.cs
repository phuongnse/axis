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
public sealed record TenantEntry(
    string Id,
    IReadOnlyList<string> Hosts,
    SecretValue ConnectionString,
    SecretValue? SchemaConnectionString,
    TenantOidc? Oidc,
    IReadOnlyDictionary<string, TenantBinding> Bindings,
    string? PreviewOf = null);

/// <summary>How one tenant signs people in. A value the source leaves blank is <c>null</c>.</summary>
public sealed record TenantOidc(
    string? Authority,
    string? ClientId,
    SecretValue? ClientSecret,
    string? EndpointAudience,
    string? UserIdClaim,
    string? DisplayNameClaim,
    string? RolesClaim);

/// <summary>
/// One deployment binding. A <c>url</c> binding has <see cref="Url"/>, as written, and a <c>secret</c> binding has
/// <see cref="Secret"/>.
/// </summary>
public sealed record TenantBinding(string Kind, string? Url, SecretValue? Secret);
