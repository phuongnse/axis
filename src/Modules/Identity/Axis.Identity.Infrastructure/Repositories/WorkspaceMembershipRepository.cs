using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceMembershipRepository(IdentityDbContext context)
    : IWorkspaceMembershipRepository
{
    public async Task AddAsync(WorkspaceMembership membership, CancellationToken ct = default) =>
        await context.WorkspaceMemberships.AddAsync(membership, ct);

    public Task<WorkspaceMembership?> GetAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default) =>
        context.WorkspaceMemberships.FirstOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.UserId == userId,
            ct);

    public Task<WorkspaceMembership?> GetActiveAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default) =>
        context.WorkspaceMemberships
            .FirstOrDefaultAsync(
                x => x.WorkspaceId == workspaceId
                    && x.UserId == userId
                    && x.Status == MembershipStatus.Active,
                ct);

    public async Task<IReadOnlyList<WorkspaceMembership>> ListActiveForUserAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .Where(x => x.UserId == userId && x.Status == MembershipStatus.Active)
            .OrderBy(x => x.WorkspaceId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveWorkspaceHumanProjection>> ListActiveForWorkspaceAsync(
        Guid workspaceId,
        CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Status == MembershipStatus.Active)
            .Join(
                context.Users.Where(user => user.Status == UserStatus.Active),
                membership => membership.UserId,
                user => user.Id,
                (_, user) => user)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Id)
            .Select(user => new ActiveWorkspaceHumanProjection(
                user.Id,
                user.FullName,
                user.Email.Value))
            .ToListAsync(ct);

    public Task<bool> HasActivePersonalOwnerWorkspaceAsync(
        Guid userId,
        CancellationToken ct = default) =>
        context.WorkspaceMemberships.AnyAsync(
            membership => membership.UserId == userId
                && membership.Role == WorkspaceMembershipRole.Owner
                && membership.Status == MembershipStatus.Active
                && context.Workspaces.Any(workspace =>
                    workspace.Id == membership.WorkspaceId
                    && workspace.Type == WorkspaceType.Personal
                    && workspace.Status == WorkspaceStatus.Active
                    && workspace.OrganizationId == null),
            ct);

    public Task<bool> HasActiveWorkspaceAccessAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default) =>
        context.WorkspaceMemberships.AnyAsync(
            membership => membership.WorkspaceId == workspaceId
                && membership.UserId == userId
                && membership.Status == MembershipStatus.Active
                && context.Workspaces.Any(workspace =>
                    workspace.Id == workspaceId
                    && workspace.Status == WorkspaceStatus.Active),
            ct);

    public async Task<IReadOnlyList<EligibleWorkspaceProjection>> ListEligibleWorkspacesAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .Where(membership =>
                membership.UserId == userId
                && membership.Status == MembershipStatus.Active)
            .Join(
                context.Workspaces.Where(workspace => workspace.Status == WorkspaceStatus.Active),
                membership => membership.WorkspaceId,
                workspace => workspace.Id,
                (_, workspace) => workspace)
            .OrderBy(workspace => workspace.Type == WorkspaceType.Personal ? 0 : 1)
            .ThenBy(workspace => workspace.Name)
            .ThenBy(workspace => workspace.Id)
            .Select(workspace => new EligibleWorkspaceProjection(
                    workspace.Id,
                    workspace.Name,
                    workspace.Slug,
                    workspace.Type,
                    workspace.OrganizationId))
            .ToListAsync(ct);
}
