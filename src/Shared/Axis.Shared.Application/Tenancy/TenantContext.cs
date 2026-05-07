namespace Axis.Shared.Application.Tenancy;

/// <summary>
/// Holds the resolved tenant information for the current request.
/// Populated by the infrastructure layer from the JWT org_id claim.
/// Schema name is cached in Redis to avoid repeated resolution.
/// </summary>
public sealed class TenantContext(Guid organizationId, string organizationSlug)
{
    public Guid OrganizationId { get; } = organizationId;
    public string OrganizationSlug { get; } = organizationSlug;

    /// <summary>
    /// The PostgreSQL schema name for this tenant. Pattern: tenant_{slug}
    /// </summary>
    public string SchemaName => $"tenant_{OrganizationSlug}";
}
