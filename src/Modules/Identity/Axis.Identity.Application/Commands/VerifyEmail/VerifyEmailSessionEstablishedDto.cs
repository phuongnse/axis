namespace Axis.Identity.Application.Commands.VerifyEmail;

/// <summary>
/// Response contract for <c>POST /api/auth/verify-email</c>: confirms the email
/// was verified and a sign-in session cookie was established for the SPA.
/// </summary>
public sealed record VerifyEmailSessionEstablishedDto(
    bool SessionEstablished,
    VerifyEmailNextStep NextStep);

public enum VerifyEmailNextStep
{
    Dashboard,
    WorkspaceProvisioning,
}
