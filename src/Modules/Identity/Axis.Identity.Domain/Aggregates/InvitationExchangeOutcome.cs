namespace Axis.Identity.Domain.Aggregates;

public enum InvitationExchangeOutcome
{
    Exchanged = 0,
    Unknown = 1,
    Expired = 2,
    Used = 3,
    Revoked = 4,
    Superseded = 5,
}
