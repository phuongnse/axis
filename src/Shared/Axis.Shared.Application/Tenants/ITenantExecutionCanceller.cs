namespace Axis.Shared.Application.Tenants;

/// <summary>Cancels in-flight workflow executions before Tenant hard-delete.</summary>
public interface ITenantExecutionCanceller
{
    Task CancelAllForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
