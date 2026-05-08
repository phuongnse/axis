namespace Axis.Shared.Application.Tenancy;

public sealed class TenantContext(Guid organizationId)
{
    public Guid OrganizationId { get; } = organizationId;

    /// <summary>
    /// PostgreSQL schema name for this tenant. Uses org ID (N format) so the
    /// schema name is stable regardless of org slug changes.
    /// </summary>
    public string SchemaName => $"tenant_{OrganizationId:N}";
}
