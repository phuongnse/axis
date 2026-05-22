using Axis.Shared.Application.Tenancy;

namespace Axis.Api.Infrastructure;

/// <summary>
/// Resolves a fixed organization for background jobs (e.g. tenant provisioning).
/// </summary>
internal sealed class FixedTenantContext(Guid organizationId) : ITenantContext
{
    public Guid OrganizationId => organizationId;
    public string SchemaName => $"tenant_{organizationId:N}";
}
