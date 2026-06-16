namespace Axis.Identity.Application.Commands.VerifyEmail;

public sealed record VerifyEmailSuccessDto(
    Guid? UserId,
    Guid? tenantId,
    string Email,
    string FullName,
    IReadOnlyList<string> Permissions,
    VerifyEmailNextStep NextStep,
    string? TenantSetupToken = null)
{
    public bool SessionEstablished => UserId.HasValue;
}
