using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Provisioning;

/// <summary>Tracks per-module workspace schema provisioning for a workspace.</summary>
public sealed class WorkspaceModuleProvisioning : Entity<(Guid workspaceId, string Module)>
{
    public Guid workspaceId { get; private set; }
    public string Module { get; private set; }
    public WorkspaceModuleProvisioningStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WorkspaceModuleProvisioning() : base(default) { Module = null!; } // EF Core

    private WorkspaceModuleProvisioning(
        Guid workspaceId,
        string module,
        WorkspaceModuleProvisioningStatus status,
        int attemptCount,
        string? lastError,
        DateTimeOffset updatedAt)
        : base((workspaceId, module))
    {
        this.workspaceId = workspaceId;
        Module = module;
        Status = status;
        AttemptCount = attemptCount;
        LastError = lastError;
        UpdatedAt = updatedAt;
    }

    public static WorkspaceModuleProvisioning CreatePending(Guid workspaceId, string module)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace id is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Module name is required.", nameof(module));

        return new(
            workspaceId,
            module,
            WorkspaceModuleProvisioningStatus.Pending,
            attemptCount: 0,
            lastError: null,
            DateTimeOffset.UtcNow);
    }

    public void RecordSuccess()
    {
        Status = WorkspaceModuleProvisioningStatus.Succeeded;
        LastError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(string error, int attemptCount)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Error message is required.", nameof(error));
        if (attemptCount < 1)
            throw new ArgumentException("Attempt count must be at least 1.", nameof(attemptCount));

        Status = WorkspaceModuleProvisioningStatus.Failed;
        AttemptCount = attemptCount;
        LastError = error;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRetryScheduled(int attemptCount)
    {
        if (attemptCount < 1)
            throw new ArgumentException("Attempt count must be at least 1.", nameof(attemptCount));

        Status = WorkspaceModuleProvisioningStatus.Pending;
        AttemptCount = attemptCount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ResetForManualRetry()
    {
        Status = WorkspaceModuleProvisioningStatus.Pending;
        AttemptCount = 0;
        LastError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
