namespace Axis.Identity.Domain.Aggregates;

public enum InvitationHandoffStatus
{
    Active = 0,
    Accepted = 1,
    Superseded = 2,
    Revoked = 3,
    Expired = 4,
}
