namespace Axis.Identity.Domain.Aggregates;

public enum InvitationTokenStatus
{
    Active = 0,
    Exchanged = 1,
    Superseded = 2,
    Revoked = 3,
    Accepted = 4,
    Expired = 5,
}
