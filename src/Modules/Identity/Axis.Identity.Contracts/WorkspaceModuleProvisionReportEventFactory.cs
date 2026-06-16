using axis.identity.events;

namespace Axis.Identity.Contracts;

public static class WorkspaceModuleProvisionReportEventFactory
{
    public static WorkspaceModuleProvisionReportEvent Create(
        Guid workspaceId,
        string module,
        bool succeeded,
        int attempt,
        string? errorMessage = null)
        => new()
        {
            workspaceId = workspaceId.ToString(),
            module = module,
            succeeded = succeeded,
            attempt = attempt,
            errorMessage = errorMessage,
        };
}
