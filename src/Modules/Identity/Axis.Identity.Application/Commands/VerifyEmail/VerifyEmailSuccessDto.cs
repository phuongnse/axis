namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed record VerifyEmailSuccessDto(
    Guid? UserId,
    Guid? OrganizationId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions,
    VerifyEmailNextStep NextStep,
    string? OrganizationSetupToken = null)
{
    public bool SessionEstablished => UserId.HasValue;
}
