namespace Axis.Identity.Application.Services;

/// <summary>notifies the platform team when workspace provisioning is exhausted.</summary>
public interface IPlatformProvisioningAlert
{
    Task AlertProvisioningFailedAsync(
        Guid workspaceId,
        string module,
        int attemptCount,
        string lastError,
        CancellationToken cancellationToken = default);
}
