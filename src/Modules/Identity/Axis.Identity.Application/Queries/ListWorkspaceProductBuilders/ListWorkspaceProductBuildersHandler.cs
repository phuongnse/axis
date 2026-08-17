using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListWorkspaceProductBuilders;

public sealed class ListWorkspaceProductBuildersHandler(
    IWorkspaceRepository workspaces,
    IWorkspaceMembershipRepository memberships)
    : IQueryHandler<ListWorkspaceProductBuildersQuery, Result<IReadOnlyList<WorkspaceProductBuilderDto>>>
{
    public async Task<Result<IReadOnlyList<WorkspaceProductBuilderDto>>> Handle(
        ListWorkspaceProductBuildersQuery query,
        CancellationToken ct)
    {
        if (query.ActorUserId == Guid.Empty || query.WorkspaceId == Guid.Empty)
            return Invalid();

        Workspace? workspace = await workspaces.GetByIdAsync(query.WorkspaceId, ct);
        if (workspace is not { Type: WorkspaceType.Organization, Status: WorkspaceStatus.Active })
            return NotFound();

        WorkspaceMembership? actor = await memberships.GetActiveHumanAsync(
            query.WorkspaceId,
            query.ActorUserId,
            ct);
        if (actor is not
            {
                Role: WorkspaceMembershipRole.Administrator,
                Status: MembershipStatus.Active,
            })
            return Forbidden();

        IReadOnlyList<ActiveWorkspaceHumanProjection> members =
            await memberships.ListActiveForWorkspaceAsync(query.WorkspaceId, ct);
        return Result.Success<IReadOnlyList<WorkspaceProductBuilderDto>>(
            members.Select(member => new WorkspaceProductBuilderDto(
                    member.UserId,
                    member.DisplayName,
                    member.Email,
                    member.WorkspaceRole.ToString(),
                    member.IsProductBuilder,
                    member.MembershipRevision,
                    member.UserId != query.ActorUserId,
                    member.Metadata ?? new ResourceMetadataDto(member.MembershipRevision, null, null, null, null)))
                .ToArray());
    }

    private static Result<IReadOnlyList<WorkspaceProductBuilderDto>> Invalid() =>
        Result.Failure<IReadOnlyList<WorkspaceProductBuilderDto>>(
            ErrorCodes.InvalidInput,
            "Actor and Workspace are required.",
            IdentityProblemCodes.ProductBuilderInvalid);

    private static Result<IReadOnlyList<WorkspaceProductBuilderDto>> Forbidden() =>
        Result.Failure<IReadOnlyList<WorkspaceProductBuilderDto>>(
            ErrorCodes.Forbidden,
            "Workspace lifecycle-administrator authority is required.",
            IdentityProblemCodes.ProductBuilderForbidden);

    private static Result<IReadOnlyList<WorkspaceProductBuilderDto>> NotFound() =>
        Result.Failure<IReadOnlyList<WorkspaceProductBuilderDto>>(
            ErrorCodes.NotFound,
            "Workspace was not found.",
            IdentityProblemCodes.ProductBuilderNotFound);
}
