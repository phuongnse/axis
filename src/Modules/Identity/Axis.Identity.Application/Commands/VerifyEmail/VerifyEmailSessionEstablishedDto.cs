namespace Axis.Identity.Application.Commands.VerifyEmail;

/// <summary>
/// Response contract for <c>POST /api/auth/verify-email</c>.
/// </summary>
public sealed record VerifyEmailSessionEstablishedDto(
    bool SessionEstablished,
    VerifyEmailNextStep NextStep,
    string? TenantSetupToken = null)
{
    public static VerifyEmailSessionEstablishedDto From(VerifyEmailSuccessDto verification) =>
        new(
            verification.SessionEstablished,
            verification.NextStep,
            verification.TenantSetupToken);
}

public enum VerifyEmailNextStep
{
    Dashboard,
    RegisterUser,
    WorkspaceProvisioning,
}
