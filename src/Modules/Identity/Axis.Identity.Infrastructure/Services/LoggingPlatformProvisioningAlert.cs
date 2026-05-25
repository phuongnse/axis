using Axis.Identity.Application.Services;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>US-003: structured critical log until external alerting is wired.</summary>
internal sealed class LoggingPlatformProvisioningAlert(ILogger<LoggingPlatformProvisioningAlert> logger)
    : IPlatformProvisioningAlert
{
    public Task AlertProvisioningFailedAsync(
        Guid organizationId,
        string module,
        int attemptCount,
        string lastError,
        CancellationToken cancellationToken = default)
    {
        logger.LogCritical(
            "Tenant provisioning failed for organization {OrganizationId} module {Module} after {AttemptCount} attempts. Last error: {LastError}",
            organizationId, module, attemptCount, lastError);
        return Task.CompletedTask;
    }
}
