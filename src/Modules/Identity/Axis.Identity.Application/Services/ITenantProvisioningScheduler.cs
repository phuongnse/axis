namespace Axis.Identity.Application.Services;

/// <summary>
/// Schedules asynchronous tenant schema provisioning after verify-email persistence (E01 US-003).
/// </summary>
public interface ITenantProvisioningScheduler
{
    Task EnqueueAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
