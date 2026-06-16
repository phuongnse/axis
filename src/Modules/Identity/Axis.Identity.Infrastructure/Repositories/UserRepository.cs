using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await context.Users.AddAsync(user, ct);

    public async Task<User?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        await context.TenantMemberships
            .Where(m => m.UserId == id && m.tenantId == tenantId)
            .Join(
                context.Users,
                membership => membership.UserId,
                user => user.Id,
                (_, user) => user)
            .FirstOrDefaultAsync(ct);

    public async Task<User?> GetByEmailAsync(Email email, Guid tenantId, CancellationToken ct = default) =>
        await context.TenantMemberships
            .Where(m => m.tenantId == tenantId)
            .Join(
                context.Users.Where(u => u.Email == email),
                membership => membership.UserId,
                user => user.Id,
                (_, user) => user)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default) =>
        await context.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

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

    public Task<int> CountActiveUsersAsync(Guid tenantId, CancellationToken ct = default) =>
        context.TenantMemberships.CountAsync(
            m => m.tenantId == tenantId && m.Status == TenantMembershipStatus.Active,
            ct);

    public async Task<IReadOnlyList<User>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await context.TenantMemberships
            .Where(m => m.tenantId == tenantId)
            .Join(
                context.Users,
                membership => membership.UserId,
                user => user.Id,
                (_, user) => user)
            .ToListAsync(ct);
}
