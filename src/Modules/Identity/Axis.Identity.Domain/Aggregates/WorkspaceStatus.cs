namespace Axis.Identity.Domain.Aggregates;

public enum WorkspaceStatus
{
    PendingVerification,
    Active,
    Provisioning,
    ProvisioningFailed,
    DeletionScheduled,
    Deleted,
    Archived,
}
