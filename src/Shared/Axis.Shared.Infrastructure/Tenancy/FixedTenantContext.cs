using Axis.Shared.Application.Tenancy;

namespace Axis.Shared.Infrastructure.Tenancy;

/// <summary>
/// Resolves a fixed team account for background jobs (e.g. tenant provisioning,
/// Wolverine handlers consuming cross-module events outside an HTTP context).
/// </summary>
public sealed class FixedTenantContext(Guid teamAccountId) : ITenantContext
{
    public Guid TeamAccountId => teamAccountId;
    public string SchemaName => $"tenant_{teamAccountId:N}";
}
