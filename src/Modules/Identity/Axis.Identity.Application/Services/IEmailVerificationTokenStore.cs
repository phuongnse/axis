namespace Axis.Identity.Application.Services;

public interface IEmailVerificationTokenStore
{
    Task CreateAsync(Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default);

    Task InvalidateAllForUserAsync(Guid userId, CancellationToken ct = default);

    Task InvalidateAsync(string tokenHash, CancellationToken ct = default);

    Task<EmailVerificationTokenResolveResult> ResolveForVerificationAsync(
        string tokenHash,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves user for provisioning poll using the same link token (including after one-time verify).
    /// </summary>
    Task<Guid?> ResolveUserIdForProvisioningPollAsync(string tokenHash, CancellationToken ct = default);
}
