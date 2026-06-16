using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed class GetProvisioningStatusHandler(
    IEmailVerificationTokenStore verificationTokenStore,
    ITenantRegistrationTokenStore TenantTokenStore,
    IUserRepository userRepo,
    ITenantMembershipRepository membershipRepo,
    ITenantRepository TenantRepo,
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
        Guid? tenantId = await TenantTokenStore.ResolvetenantIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (tenantId is Guid resolvedtenantId)
            return await BuildStatusAsync(resolvedtenantId, cancellationToken);

        Guid? userId = await verificationTokenStore.ResolveUserIdForProvisioningPollAsync(
            tokenHash,
            cancellationToken);
        if (userId is null)
            return null;

        User? user = await userRepo.GetByIdPlatformWideAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return null;

        TenantMembership? membership =
            await membershipRepo.GetFirstActiveByUserIdAsync(user.Id, cancellationToken);
        if (membership is null)
            return null;

        return await BuildStatusAsync(membership.tenantId, cancellationToken);
    }

    private async Task<ProvisioningStatusDto?> BuildStatusAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Tenant? Tenant = await TenantRepo.GetByIdAsync(tenantId, cancellationToken);
        if (Tenant is null)
            return null;

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForTenantAsync(tenantId, cancellationToken);

        bool isReady = Tenant.Status == TenantStatus.Active
            && TenantModuleNames.All.All(moduleName =>
                modules.Any(m =>
                    m.Module == moduleName
                    && m.Status == TenantModuleProvisioningStatus.Succeeded));

        return new ProvisioningStatusDto(
            Tenant.Id,
            Tenant.Status.ToString(),
            isReady,
            modules.Select(m => new ModuleProvisioningStatusDto(
                m.Module,
                m.Status.ToString(),
                m.AttemptCount,
                m.LastError)).ToList());
    }
}
