using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed class GetProvisioningStatusHandler(
    IEmailVerificationTokenStore verificationTokenStore,
    IOrganizationRegistrationTokenStore organizationTokenStore,
    IUserRepository userRepo,
    IOrganizationMembershipRepository membershipRepo,
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo)
    : IQueryHandler<GetProvisioningStatusQuery, ProvisioningStatusDto?>
{
    public async Task<ProvisioningStatusDto?> Handle(
        GetProvisioningStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Token))
            return null;

        string tokenHash = OpaqueTokenGenerator.Hash(query.Token.Trim());
        Guid? organizationId = await organizationTokenStore.ResolveOrganizationIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (organizationId is Guid resolvedOrganizationId)
            return await BuildStatusAsync(resolvedOrganizationId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return null;

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return null;

        OrganizationMembership? membership =
            await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);
        if (membership is null)
            return null;

        return await BuildStatusAsync(membership.OrganizationId, cancellationToken);
    }

    private async Task<ProvisioningStatusDto?> BuildStatusAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        Organization? organization = await organizationRepo.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return null;

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForOrganizationAsync(organizationId, cancellationToken);

        bool isReady = organization.Status == OrganizationStatus.Active
            && TenantModuleNames.All.All(moduleName =>
                modules.Any(m =>
                    m.Module == moduleName
                    && m.Status == TenantModuleProvisioningStatus.Succeeded));

        return new ProvisioningStatusDto(
            organization.Id,
            organization.Status.ToString(),
            isReady,
            modules.Select(m => new ModuleProvisioningStatusDto(
                m.Module,
                m.Status.ToString(),
                m.AttemptCount,
                m.LastError)).ToList());
    }
}
