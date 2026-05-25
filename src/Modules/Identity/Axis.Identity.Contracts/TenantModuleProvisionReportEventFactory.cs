using axis.identity.events;

namespace Axis.Identity.Contracts;

public static class TenantModuleProvisionReportEventFactory
{
    public static TenantModuleProvisionReportEvent Create(
        Guid organizationId,
        string module,
        bool succeeded,
        int attempt,
        string? errorMessage = null)
        => new()
        {
            organizationId = organizationId.ToString(),
            module = module,
            succeeded = succeeded,
            attempt = attempt,
            errorMessage = errorMessage,
        };
}
