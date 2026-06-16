namespace Axis.Identity.Domain.Aggregates;

public enum TeamAccountStatus
{
    PendingVerification,
    Active,
    Provisioning,
    ProvisioningFailed,
    DeletionScheduled,
    Deleted,
    Archived,
}
