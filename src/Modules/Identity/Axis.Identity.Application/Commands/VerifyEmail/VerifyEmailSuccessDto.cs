namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed record VerifyEmailSuccessDto(
    Guid? UserId,
    Guid? TeamAccountId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions,
    VerifyEmailNextStep NextStep,
    string? TeamAccountSetupToken = null)
{
    public bool SessionEstablished => UserId.HasValue;
}
