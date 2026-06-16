using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TenantMembershipRepository(IdentityDbContext context) : ITenantMembershipRepository
{
    public async Task AddAsync(TenantMembership membership, CancellationToken ct = default) =>
        await context.TenantMemberships.AddAsync(membership, ct);

    public Task<TenantMembership?> GetByUserAndTenantAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default) =>
        context.TenantMemberships
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.tenantId == tenantId,
                ct);

    public Task<TenantMembership?> GetFirstActiveByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        context.TenantMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId && m.Status == TenantMembershipStatus.Active)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TenantMembership>> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await context.TenantMemberships
            .Include(m => m.Roles)
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

    public Task<int> CountActiveUsersAsync(Guid tenantId, CancellationToken ct = default) =>
        context.TenantMemberships.CountAsync(
            m => m.tenantId == tenantId && m.Status == TenantMembershipStatus.Active,
            ct);

    public Task<int> CountAdminsAsync(Guid tenantId, Guid adminRoleId, CancellationToken ct = default) =>
        context.Set<TenantMembershipRole>()
            .Join(
                context.TenantMemberships,
                role => role.MembershipId,
                membership => membership.Id,
                (role, membership) => new { role, membership })
            .CountAsync(
                row => row.role.RoleId == adminRoleId
                       && row.membership.tenantId == tenantId
                       && row.membership.Status == TenantMembershipStatus.Active,
                ct);
}
