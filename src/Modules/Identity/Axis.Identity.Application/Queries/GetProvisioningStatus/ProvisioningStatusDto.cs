namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed record ProvisioningStatusDto(
    Guid TeamAccountId,
    string TeamAccountStatus,
    bool IsReady,
    IReadOnlyList<ModuleProvisioningStatusDto> Modules);

public sealed record ModuleProvisioningStatusDto(
    string Module,
    string Status,
    int AttemptCount,
    string? LastError);
