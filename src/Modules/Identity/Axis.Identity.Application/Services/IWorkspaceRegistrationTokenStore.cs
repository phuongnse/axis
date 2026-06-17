using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface IWorkspaceRegistrationTokenStore
{
    Task CreateVerificationAsync(
        Guid workspaceId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task<Guid?> ResolveWorkspaceIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task CreateFirstUserSetupAsync(
        Guid workspaceId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default);
}
