using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
namespace Axis.Identity.Application.Queries.ListEligibleWorkspaces;

public sealed class ListEligibleWorkspacesHandler(IWorkspaceMembershipRepository memberships, IWorkspaceRepository workspaces) : IQueryHandler<ListEligibleWorkspacesQuery, IReadOnlyList<EligibleWorkspaceDto>>
{ public async Task<IReadOnlyList<EligibleWorkspaceDto>> Handle(ListEligibleWorkspacesQuery query, CancellationToken ct) { IReadOnlyList<WorkspaceMembership> granted = await memberships.ListActiveForUserAsync(query.UserId, ct); List<EligibleWorkspaceDto> result = []; foreach (WorkspaceMembership membership in granted) { Workspace? workspace = await workspaces.GetByIdAsync(membership.WorkspaceId, ct); if (workspace?.Status == WorkspaceStatus.Active) result.Add(new(workspace.Id, workspace.Name, workspace.Slug.Value, workspace.Type.ToString(), workspace.OrganizationId, query.CurrentWorkspaceId == workspace.Id)); } return result.OrderBy(x => x.Type == WorkspaceType.Personal.ToString() ? 0 : 1).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); } }
