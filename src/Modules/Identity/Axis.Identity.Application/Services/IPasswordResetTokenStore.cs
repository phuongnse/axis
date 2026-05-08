namespace Axis.Identity.Application.Services;

public interface IPasswordResetTokenStore
{
    /// <summary>Stores a new password reset token (hashed) for the given user.</summary>
    Task CreateAsync(Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default);

    /// <summary>Returns the userId for a valid (non-expired, non-used) token hash, or null if invalid.</summary>
    Task<Guid?> FindUserIdByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Marks a token as used so it cannot be replayed.</summary>
    Task InvalidateAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Invalidates all existing reset tokens for the user (e.g. second request before first expires).</summary>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken ct = default);
}
