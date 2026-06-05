namespace Axis.Identity.Application.Services;

public sealed record OrganizationVerificationTokenResolveResult(
    OrganizationVerificationTokenState State,
    Guid? OrganizationId);

public enum OrganizationVerificationTokenState
{
    Valid,
    NotFound,
    Expired,
    AlreadyUsed,
}

public sealed record OrganizationSetupTokenConsumeResult(
    OrganizationSetupTokenState State,
    Guid? OrganizationId);

public enum OrganizationSetupTokenState
{
    Valid,
    NotFound,
    Expired,
    AlreadyUsed,
}
