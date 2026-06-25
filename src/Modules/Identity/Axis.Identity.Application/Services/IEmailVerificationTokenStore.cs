namespace Axis.Identity.Application.Services;

public interface IEmailVerificationTokenStore
{
    Task CreateAsync(Guid userId, string tokenHash, DateTime expiresAt, CancellationToken ct = default);

    Task InvalidateAllForUserAsync(Guid userId, CancellationToken ct = default);

    Task InvalidateAsync(string tokenHash, CancellationToken ct = default);

    Task<EmailVerificationTokenResolveResult> ResolveForVerificationAsync(
        string tokenHash,
        CancellationToken ct = default);
}
