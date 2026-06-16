namespace Axis.Shared.Application.Tenants;

/// <summary>Cancels pending form tasks before Tenant hard-delete.</summary>
public interface ITenantFormTaskCanceller
{
    Task CancelPendingForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
