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

    public async Task<User?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .Where(m => m.UserId == id && m.workspaceId == workspaceId)
            .Join(
                context.Users,
                membership => membership.UserId,
                user => user.Id,
                (_, user) => user)
            .FirstOrDefaultAsync(ct);

    public async Task<User?> GetByEmailAsync(Email email, Guid workspaceId, CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .Where(m => m.workspaceId == workspaceId)
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

    public Task<int> CountActiveUsersAsync(Guid workspaceId, CancellationToken ct = default) =>
        context.WorkspaceMemberships.CountAsync(
            m => m.workspaceId == workspaceId && m.Status == WorkspaceMembershipStatus.Active,
            ct);

    public async Task<IReadOnlyList<User>> GetAllByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        await context.WorkspaceMemberships
            .Where(m => m.workspaceId == workspaceId)
            .Join(
                context.Users,
                membership => membership.UserId,
                user => user.Id,
                (_, user) => user)
            .ToListAsync(ct);
}
