namespace Axis.Identity.Domain.Aggregates;

public enum OrganizationStatus
{
    Active,
    Provisioning,
    ProvisioningFailed,
    DeletionScheduled,
    Deleted,
    Archived,
}
