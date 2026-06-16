using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TeamAccountMembershipRepository(IdentityDbContext context) : ITeamAccountMembershipRepository
{
    public async Task AddAsync(TeamAccountMembership membership, CancellationToken ct = default) =>
        await context.TeamAccountMemberships.AddAsync(membership, ct);

    public Task<TeamAccountMembership?> GetByUserAndTeamAccountAsync(
        Guid userId,
        Guid teamAccountId,
        CancellationToken ct = default) =>
        context.TeamAccountMemberships
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.TeamAccountId == teamAccountId,
                ct);

    public Task<TeamAccountMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        context.TeamAccountMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId && m.Status == TeamAccountMembershipStatus.Active)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TeamAccountMembership>> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await context.TeamAccountMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

    public Task<int> CountActiveUsersAsync(Guid teamAccountId, CancellationToken ct = default) =>
        context.TeamAccountMemberships.CountAsync(
            m => m.TeamAccountId == teamAccountId && m.Status == TeamAccountMembershipStatus.Active,
            ct);

    public Task<int> CountAdminsAsync(Guid teamAccountId, Guid adminRoleId, CancellationToken ct = default) =>
        context.Set<TeamAccountMembershipRole>()
            .Join(
                context.TeamAccountMemberships,
                role => role.MembershipId,
                membership => membership.Id,
                (role, membership) => new { role, membership })
            .CountAsync(
                row => row.role.RoleId == adminRoleId
                       && row.membership.TeamAccountId == teamAccountId
                       && row.membership.Status == TeamAccountMembershipStatus.Active,
                ct);
}
