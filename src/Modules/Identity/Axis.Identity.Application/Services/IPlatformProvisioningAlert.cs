namespace Axis.Identity.Application.Services;

/// <summary>notifies the platform team when tenant provisioning is exhausted.</summary>
public interface IPlatformProvisioningAlert
{
    Task AlertProvisioningFailedAsync(
        Guid organizationId,
        string module,
        int attemptCount,
        string lastError,
        CancellationToken cancellationToken = default);
}
