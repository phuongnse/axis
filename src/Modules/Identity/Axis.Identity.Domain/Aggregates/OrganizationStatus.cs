namespace Axis.Identity.Domain.Aggregates;

public enum OrganizationStatus
{
    PendingVerification,
    Active,
    Provisioning,
    ProvisioningFailed,
    DeletionScheduled,
    Deleted,
    Archived,
}
