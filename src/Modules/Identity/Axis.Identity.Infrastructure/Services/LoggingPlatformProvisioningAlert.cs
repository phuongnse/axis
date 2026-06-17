using Axis.Identity.Application.Services;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>structured critical log until external alerting is wired.</summary>
internal sealed class LoggingPlatformProvisioningAlert(ILogger<LoggingPlatformProvisioningAlert> logger)
    : IPlatformProvisioningAlert
{
    public Task AlertProvisioningFailedAsync(
        Guid workspaceId,
        string module,
        int attemptCount,
        string lastError,
        CancellationToken cancellationToken = default)
    {
        logger.LogCritical(
            "Workspace provisioning failed for module {Module} after {AttemptCount} attempts. Last error: {LastError}",
            module,
            attemptCount,
            lastError);
        return Task.CompletedTask;
    }
}
