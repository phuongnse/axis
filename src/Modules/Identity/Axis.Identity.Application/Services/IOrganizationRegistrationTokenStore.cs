namespace Axis.Identity.Application.Services;

public interface IOrganizationRegistrationTokenStore
{
    Task CreateVerificationAsync(
        Guid organizationId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken ct = default);

    Task<OrganizationVerificationTokenResolveResult> ResolveVerificationAsync(
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

    Task<OrganizationSetupTokenConsumeResult> ConsumeFirstUserSetupAsync(
        string tokenHash,
        Guid userId,
        CancellationToken ct = default);
}
