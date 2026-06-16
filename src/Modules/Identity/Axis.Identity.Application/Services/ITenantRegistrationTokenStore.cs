using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface ITenantRegistrationTokenStore
{
    Task CreateVerificationAsync(
        Guid tenantId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task<Guid?> ResolvetenantIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task CreateFirstUserSetupAsync(
        Guid tenantId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default);
}
