namespace Axis.Core.Tenancy;

/// <summary>The tenant that the current request or job runs for.</summary>
/// <param name="TenantId">The tenant id.</param>
/// <param name="SitePath">The lower-case path of the site whose own host the request came on, or <c>null</c> when the host belongs to no site or the context is not a request.</param>
/// <param name="ReadOnly">Whether the tenant is read-only at the moment the request resolved it.</param>
public sealed record TenantContext(string TenantId, string? SitePath = null, bool ReadOnly = false);
