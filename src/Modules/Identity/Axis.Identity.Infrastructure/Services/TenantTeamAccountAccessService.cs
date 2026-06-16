using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class TenantTeamAccountAccessService(ITeamAccountRepository teamAccountRepository)
    : ITenantTeamAccountAccessService
{
    public async Task<Result> EvaluateAsync(
        Guid teamAccountId,
        CancellationToken cancellationToken = default)
    {
        TeamAccount? teamAccount = await teamAccountRepository.GetByIdAsync(teamAccountId, cancellationToken);
        if (teamAccount is null)
            return Result.Failure(ErrorCodes.Forbidden, "Team account is not available.");

        if (teamAccount.Status is TeamAccountStatus.Deleted or TeamAccountStatus.Archived)
            return Result.Failure(ErrorCodes.Forbidden, "Team account is not available.");

        if (!teamAccount.AllowsTenantDataAccess())
            return Result.Failure(
                ErrorCodes.Forbidden,
                "Workspace is still being set up. Try again shortly.");

        return Result.Success();
    }
}
