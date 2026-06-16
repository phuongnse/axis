using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface ITeamAccountRegistrationTokenStore
{
    Task CreateVerificationAsync(
        Guid teamAccountId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task<Guid?> ResolveTeamAccountIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task CreateFirstUserSetupAsync(
        Guid teamAccountId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default);
}
