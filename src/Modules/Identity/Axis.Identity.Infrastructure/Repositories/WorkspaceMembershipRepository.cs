using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceMembershipRepository(IdentityDbContext context) : IWorkspaceMembershipRepository
{
    public async Task AddAsync(WorkspaceMembership membership, CancellationToken ct = default) =>
        await context.WorkspaceMemberships.AddAsync(membership, ct);

    public Task<WorkspaceMembership?> GetByUserAndWorkspaceAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default) =>
        context.WorkspaceMemberships
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.workspaceId == workspaceId,
                ct);

    public Task<WorkspaceMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        context.WorkspaceMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId && m.Status == WorkspaceMembershipStatus.Active)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<WorkspaceMembership>> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

    public Task<int> CountActiveUsersAsync(Guid workspaceId, CancellationToken ct = default) =>
        context.WorkspaceMemberships.CountAsync(
            m => m.workspaceId == workspaceId && m.Status == WorkspaceMembershipStatus.Active,
            ct);

    public Task<int> CountAdminsAsync(Guid workspaceId, Guid adminRoleId, CancellationToken ct = default) =>
        context.Set<WorkspaceMembershipRole>()
            .Join(
                context.WorkspaceMemberships,
                role => role.MembershipId,
                membership => membership.Id,
                (role, membership) => new { role, membership })
            .CountAsync(
                row => row.role.RoleId == adminRoleId
                       && row.membership.workspaceId == workspaceId
                       && row.membership.Status == WorkspaceMembershipStatus.Active,
                ct);
}
