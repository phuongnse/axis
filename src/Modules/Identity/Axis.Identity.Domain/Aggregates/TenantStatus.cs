namespace Axis.Identity.Domain.Aggregates;

public enum TenantStatus
{
    PendingVerification,
    Active,
    Provisioning,
    ProvisioningFailed,
    DeletionScheduled,
    Deleted,
    Archived,
}
