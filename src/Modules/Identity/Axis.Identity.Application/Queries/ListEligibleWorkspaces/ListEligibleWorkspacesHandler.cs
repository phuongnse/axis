using Axis.Identity.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.ListEligibleWorkspaces;

public sealed class ListEligibleWorkspacesHandler(IWorkspaceMembershipRepository memberships)
    : IQueryHandler<ListEligibleWorkspacesQuery, IReadOnlyList<EligibleWorkspaceDto>>
{
    public async Task<IReadOnlyList<EligibleWorkspaceDto>> Handle(
        ListEligibleWorkspacesQuery query,
        CancellationToken ct)
    {
        IReadOnlyList<EligibleWorkspaceProjection> eligible =
            await memberships.ListEligibleWorkspacesAsync(query.UserId, ct);

        return eligible.Select(workspace => new EligibleWorkspaceDto(
                workspace.WorkspaceId,
                workspace.Name,
                workspace.Slug.Value,
                workspace.Type.ToString(),
                workspace.OrganizationId,
                query.CurrentWorkspaceId == workspace.WorkspaceId))
            .ToList();
    }
}
