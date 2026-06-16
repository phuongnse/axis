using Axis.Shared.Application.Tenancy;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Resolves a fixed Tenant for background jobs (e.g. tenant provisioning,
/// Wolverine handlers consuming cross-module events outside an HTTP context).
/// </summary>
public sealed class FixedTenantContext(Guid tenantId) : ITenantContext
{
    public Guid tenantId { get; } = tenantId;
    public string SchemaName => $"tenant_{tenantId:N}";
}
