using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class OrganizationMembershipRepository(IdentityDbContext context) : IOrganizationMembershipRepository
{
    public async Task AddAsync(OrganizationMembership membership, CancellationToken ct = default) =>
        await context.OrganizationMemberships.AddAsync(membership, ct);

    public Task<OrganizationMembership?> GetByUserAndOrganizationAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default) =>
        context.OrganizationMemberships
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.OrganizationId == organizationId,
                ct);

    public Task<OrganizationMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        context.OrganizationMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId && m.Status == OrganizationMembershipStatus.Active)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OrganizationMembership>> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await context.OrganizationMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

    public Task<int> CountActiveUsersAsync(Guid organizationId, CancellationToken ct = default) =>
        context.OrganizationMemberships.CountAsync(
            m => m.OrganizationId == organizationId && m.Status == OrganizationMembershipStatus.Active,
            ct);

    public Task<int> CountAdminsAsync(Guid organizationId, Guid adminRoleId, CancellationToken ct = default) =>
        context.Set<OrganizationMembershipRole>()
            .Join(
                context.OrganizationMemberships,
                role => role.MembershipId,
                membership => membership.Id,
                (role, membership) => new { role, membership })
            .CountAsync(
                row => row.role.RoleId == adminRoleId
                       && row.membership.OrganizationId == organizationId
                       && row.membership.Status == OrganizationMembershipStatus.Active,
                ct);
}
