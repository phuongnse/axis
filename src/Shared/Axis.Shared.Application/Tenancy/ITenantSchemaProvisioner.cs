namespace Axis.Shared.Application.Tenancy;

/// <summary>
/// Creates the per-organization PostgreSQL schema and applies module migrations (US-003).
/// </summary>
public interface ITenantSchemaProvisioner
{
    Task ProvisionAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
