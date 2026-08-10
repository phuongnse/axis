namespace Axis.Identity.Domain.Aggregates;

public enum InvitationAcceptanceOutcome
{
    Accepted = 0,
    Unknown = 1,
    Expired = 2,
    Used = 3,
    Revoked = 4,
    Superseded = 5,
}
