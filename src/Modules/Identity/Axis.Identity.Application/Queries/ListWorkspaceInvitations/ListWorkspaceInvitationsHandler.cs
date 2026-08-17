using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListWorkspaceInvitations;

public sealed class ListWorkspaceInvitationsHandler(
    IWorkspaceRepository workspaces,
    IOrganizationMembershipRepository organizationMemberships,
    IWorkspaceMembershipRepository workspaceMemberships,
    IWorkspaceInvitationRepository invitations,
    WorkspaceInvitationPolicy policy)
    : IQueryHandler<ListWorkspaceInvitationsQuery, Result<WorkspaceInvitationPageDto>>
{
    public async Task<Result<WorkspaceInvitationPageDto>> Handle(
        ListWorkspaceInvitationsQuery query,
        CancellationToken ct)
    {
        if (query.Page <= 0 || query.PageSize <= 0 || query.PageSize > policy.MaximumPageSize)
        {
            return Result.Failure<WorkspaceInvitationPageDto>(
                ErrorCodes.InvalidInput,
                $"Page must be positive and pageSize cannot exceed {policy.MaximumPageSize}.",
                IdentityProblemCodes.InvitationPageInvalid);
        }

        if ((query.SortBy.HasValue && !Enum.IsDefined(query.SortBy.Value))
            || (query.SortDirection.HasValue && !Enum.IsDefined(query.SortDirection.Value))
            || (query.SortBy.HasValue != query.SortDirection.HasValue))
        {
            return Result.Failure<WorkspaceInvitationPageDto>(
                ErrorCodes.InvalidInput,
                "Invitation sort is invalid.",
                IdentityProblemCodes.InvitationPageInvalid);
        }

        Workspace? workspace = await workspaces.GetByIdAsync(query.WorkspaceId, ct);
        if (workspace?.OrganizationId is not Guid organizationId)
            return NotFound();
        if (await organizationMemberships.GetActiveAsync(organizationId, query.ActorUserId, ct) is null
            || await workspaceMemberships.GetActiveAsync(query.WorkspaceId, query.ActorUserId, ct) is not
            {
                Role: WorkspaceMembershipRole.Administrator,
                Status: MembershipStatus.Active,
            })
        {
            return Result.Failure<WorkspaceInvitationPageDto>(
                ErrorCodes.Forbidden,
                "Invitation authority is required.",
                IdentityProblemCodes.InvitationForbidden);
        }

        int offset = checked((query.Page - 1) * query.PageSize);
        IReadOnlyList<WorkspaceInvitation> rows = await invitations.ListForWorkspaceAsync(
            query.WorkspaceId,
            offset,
            query.PageSize,
            query.SortBy,
            query.SortDirection,
            ct);
        int total = await invitations.CountForWorkspaceAsync(query.WorkspaceId, ct);
        return Result.Success(new WorkspaceInvitationPageDto(
            rows.Select(invitation => invitation.ToLifecycleDto()).ToList(),
            query.Page,
            query.PageSize,
            total));
    }

    private static Result<WorkspaceInvitationPageDto> NotFound() =>
        Result.Failure<WorkspaceInvitationPageDto>(
            ErrorCodes.NotFound,
            "Workspace was not found.",
            IdentityProblemCodes.InvitationNotFound);
}
