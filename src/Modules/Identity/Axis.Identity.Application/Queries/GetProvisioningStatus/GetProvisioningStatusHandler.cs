using Axis.Identity.Application.Repositories;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Provisioning;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetProvisioningStatus;

public sealed class GetProvisioningStatusHandler(
    IUserRepository userRepo,
    IOrganizationRepository organizationRepo,
    ITenantModuleProvisioningRepository provisioningRepo)
    : IQueryHandler<GetProvisioningStatusQuery, ProvisioningStatusDto?>
{
    public async Task<ProvisioningStatusDto?> Handle(
        GetProvisioningStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(query.Token, out Guid userId))
            return null;

        Domain.Aggregates.User? user = await userRepo.GetByIdPlatformWideAsync(userId, cancellationToken);
        if (user is null || !user.IsEmailVerified)
            return null;

        Organization? organization = await organizationRepo.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (organization is null)
            return null;

        IReadOnlyList<TenantModuleProvisioning> modules =
            await provisioningRepo.GetAllForOrganizationAsync(user.OrganizationId, cancellationToken);

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
