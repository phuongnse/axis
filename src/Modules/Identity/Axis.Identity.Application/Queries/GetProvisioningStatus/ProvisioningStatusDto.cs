namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed record ProvisioningStatusDto(
    Guid workspaceId,
    string WorkspaceStatus,
    bool IsReady,
    IReadOnlyList<ModuleProvisioningStatusDto> Modules);

public sealed record ModuleProvisioningStatusDto(
    string Module,
    string Status,
    int AttemptCount,
    string? LastError);
