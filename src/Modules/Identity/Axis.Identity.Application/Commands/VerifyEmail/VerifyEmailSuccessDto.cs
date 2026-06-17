namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed record VerifyEmailSuccessDto(
    Guid? UserId,
    Guid? workspaceId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions,
    VerifyEmailNextStep NextStep,
    string? WorkspaceSetupToken = null)
{
    public bool SessionEstablished => UserId.HasValue;
}
