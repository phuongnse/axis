namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed record VerifyEmailSuccessDto(
    Guid? UserId,
    Guid? workspaceId,
    string Email,
    string FullName,
    VerifyEmailNextStep NextStep)
{
    public bool SessionEstablished => UserId.HasValue;
}
