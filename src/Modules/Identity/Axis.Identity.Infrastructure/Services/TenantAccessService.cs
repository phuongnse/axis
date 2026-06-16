using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TenantAccessService(ITenantRepository TenantRepository)
    : ITenantAccessService
{
    public async Task<Result> EvaluateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        Tenant? Tenant = await TenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (Tenant is null)
            return Result.Failure(ErrorCodes.Forbidden, "Tenant is not available.");

        if (Tenant.Status is TenantStatus.Deleted or TenantStatus.Archived)
            return Result.Failure(ErrorCodes.Forbidden, "Tenant is not available.");

        if (!Tenant.AllowsTenantDataAccess())
            return Result.Failure(
                ErrorCodes.Forbidden,
                "Workspace is still being set up. Try again shortly.");

        return Result.Success();
    }
}
