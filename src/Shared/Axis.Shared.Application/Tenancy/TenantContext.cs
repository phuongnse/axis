namespace Axis.Shared.Application.Tenancy;

public sealed class TenantContext(Guid teamAccountId)
{
    public Guid TeamAccountId { get; } = teamAccountId;

    /// <summary>
    /// PostgreSQL schema name for this tenant. Uses team account ID (N format) so the
    /// schema name is stable regardless of team account slug changes.
    /// </summary>
    public string SchemaName => $"tenant_{TeamAccountId:N}";
}
