namespace Axis.Identity.Application.Commands.AuthenticateUser;

public record AuthenticationResult
{
    public bool Success { get; init; }
    public AuthFailureReason? FailureReason { get; init; }
    public DateTime? LockedUntil { get; init; }

    // Populated on success
    public Guid UserId { get; init; }
    public Guid OrganizationId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; init; } = [];

    public static AuthenticationResult Fail(AuthFailureReason reason, DateTime? lockedUntil = null) =>
        new() { Success = false, FailureReason = reason, LockedUntil = lockedUntil };

    public static AuthenticationResult Ok(
        Guid userId, Guid orgId, string email, string fullName, IReadOnlyList<string> permissions) =>
        new()
        {
            Success = true,
            UserId = userId,
            OrganizationId = orgId,
            Email = email,
            FullName = fullName,
            Permissions = permissions,
        };
}
