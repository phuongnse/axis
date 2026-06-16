namespace Axis.Shared.Application.Tenancy;

public sealed class TenantContext(Guid tenantId)
{
    public Guid tenantId { get; } = tenantId;

    /// <summary>
    /// PostgreSQL schema name for this tenant. Uses Tenant ID (N format) so the
    /// schema name is stable regardless of Tenant slug changes.
    /// </summary>
    public string SchemaName => $"tenant_{tenantId:N}";
}
