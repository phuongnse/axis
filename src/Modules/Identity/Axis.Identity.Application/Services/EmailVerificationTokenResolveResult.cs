namespace Axis.Identity.Application.Services;

public enum EmailVerificationTokenState
{
    NotFound = 0,
    Valid = 1,
    Expired = 2,
    AlreadyUsed = 3,
}

public sealed record EmailVerificationTokenResolveResult(
    EmailVerificationTokenState State,
    Guid? UserId);
