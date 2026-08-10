namespace Axis.Identity.Domain.Aggregates;

public enum WorkspaceInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3,
}
