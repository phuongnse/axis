using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TenantOrganizationAccessService(IOrganizationRepository organizationRepository)
    : ITenantOrganizationAccessService
{
    public async Task<Result> EvaluateAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        Organization? organization = await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return Result.Failure(ErrorCodes.Forbidden, "Organization is not available.");

        if (organization.Status is OrganizationStatus.Deleted or OrganizationStatus.Archived)
            return Result.Failure(ErrorCodes.Forbidden, "Organization is not available.");

        if (!organization.AllowsTenantDataAccess())
            return Result.Failure(
                ErrorCodes.Forbidden,
                "Workspace is still being set up. Try again shortly.");

        return Result.Success();
    }
}
