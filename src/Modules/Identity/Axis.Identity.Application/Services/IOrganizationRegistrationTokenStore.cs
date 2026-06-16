using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface IOrganizationRegistrationTokenStore
{
    Task CreateVerificationAsync(
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ResolveVerificationAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task<Guid?> ResolveOrganizationIdForProvisioningPollAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task CreateFirstUserSetupAsync(
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<Result<Guid>> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default);
}
