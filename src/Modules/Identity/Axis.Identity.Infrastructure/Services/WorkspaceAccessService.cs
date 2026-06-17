using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class WorkspaceAccessService(IWorkspaceRepository WorkspaceRepository)
    : IWorkspaceAccessService
{
    public async Task<Result> EvaluateAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        Workspace? Workspace = await WorkspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
        if (Workspace is null)
            return Result.Failure(ErrorCodes.Forbidden, "Workspace is not available.");

        if (Workspace.Status is WorkspaceStatus.Deleted or WorkspaceStatus.Archived)
            return Result.Failure(ErrorCodes.Forbidden, "Workspace is not available.");

        if (!Workspace.AllowsWorkspaceDataAccess())
            return Result.Failure(
                ErrorCodes.Forbidden,
                "Workspace is still being set up. Try again shortly.");

        return Result.Success();
    }
}
