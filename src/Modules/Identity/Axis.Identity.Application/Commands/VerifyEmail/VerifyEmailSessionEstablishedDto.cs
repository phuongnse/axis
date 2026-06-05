namespace Axis.Identity.Application.Commands.VerifyEmail;

/// <summary>
/// Response contract for <c>POST /api/auth/verify-email</c>.
/// </summary>
public sealed record VerifyEmailSessionEstablishedDto(
    bool SessionEstablished,
    VerifyEmailNextStep NextStep,
    string? OrganizationSetupToken = null);

public enum VerifyEmailNextStep
{
    Dashboard,
    RegisterUser,
    WorkspaceProvisioning,
}
