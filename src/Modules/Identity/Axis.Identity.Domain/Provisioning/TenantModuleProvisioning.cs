using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Provisioning;

/// <summary>Tracks per-module tenant schema provisioning for a tenant.</summary>
public sealed class TenantModuleProvisioning : Entity<(Guid tenantId, string Module)>
{
    public Guid tenantId { get; private set; }
    public string Module { get; private set; }
    public TenantModuleProvisioningStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantModuleProvisioning() : base(default) { Module = null!; } // EF Core

    private TenantModuleProvisioning(
        Guid tenantId,
        string module,
        TenantModuleProvisioningStatus status,
        int attemptCount,
        string? lastError,
        DateTimeOffset updatedAt)
        : base((tenantId, module))
    {
        this.tenantId = tenantId;
        Module = module;
        Status = status;
        AttemptCount = attemptCount;
        LastError = lastError;
        UpdatedAt = updatedAt;
    }

    public static TenantModuleProvisioning CreatePending(Guid tenantId, string module)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Module name is required.", nameof(module));

        return new(
            tenantId,
            module,
            TenantModuleProvisioningStatus.Pending,
            attemptCount: 0,
            lastError: null,
            DateTimeOffset.UtcNow);
    }

    public void RecordSuccess()
    {
        Status = TenantModuleProvisioningStatus.Succeeded;
        LastError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(string error, int attemptCount)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Error message is required.", nameof(error));
        if (attemptCount < 1)
            throw new ArgumentException("Attempt count must be at least 1.", nameof(attemptCount));

        Status = TenantModuleProvisioningStatus.Failed;
        AttemptCount = attemptCount;
        LastError = error;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRetryScheduled(int attemptCount)
    {
        if (attemptCount < 1)
            throw new ArgumentException("Attempt count must be at least 1.", nameof(attemptCount));

        Status = TenantModuleProvisioningStatus.Pending;
        AttemptCount = attemptCount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ResetForManualRetry()
    {
        Status = TenantModuleProvisioningStatus.Pending;
        AttemptCount = 0;
        LastError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
