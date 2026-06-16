using Axis.Shared.Application.Tenancy;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Resolves a fixed organization for background jobs (e.g. tenant provisioning,
/// Wolverine handlers consuming cross-module events outside an HTTP context).
/// </summary>
public sealed class FixedTenantContext(Guid organizationId) : ITenantContext
{
    public Guid OrganizationId => organizationId;
    public string SchemaName => $"tenant_{organizationId:N}";
}
