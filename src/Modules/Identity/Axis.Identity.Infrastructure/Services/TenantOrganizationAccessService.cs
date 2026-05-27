using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TenantOrganizationAccessService(IOrganizationRepository organizationRepository)
    : ITenantOrganizationAccessService
{
    public async Task<TenantOrganizationAccessResult> EvaluateAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        Organization? organization = await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return new TenantOrganizationAccessResult(TenantOrganizationAccessStatus.OrganizationNotFound);

        if (organization.Status is OrganizationStatus.Deleted or OrganizationStatus.Archived)
            return new TenantOrganizationAccessResult(TenantOrganizationAccessStatus.OrganizationSuspended);

        if (!organization.AllowsTenantDataAccess())
            return new TenantOrganizationAccessResult(TenantOrganizationAccessStatus.OrganizationNotReady);

        return new TenantOrganizationAccessResult(TenantOrganizationAccessStatus.Allowed);
    }
}
