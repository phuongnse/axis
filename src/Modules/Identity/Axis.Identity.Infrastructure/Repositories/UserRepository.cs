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

    public async Task<User?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default) =>
        await context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == organizationId, ct);

    public async Task<User?> GetByEmailAsync(Email email, Guid organizationId, CancellationToken ct = default) =>
        await context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.OrganizationId == organizationId, ct);

    public async Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default) =>
        await context.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<int> CountAdminsAsync(Guid organizationId, Guid adminRoleId, CancellationToken ct = default) =>
        await context.Users
            .Where(u => u.OrganizationId == organizationId
                     && EF.Property<List<Guid>>(u, "_roleIds").Contains(adminRoleId))
            .CountAsync(ct);

    public Task<int> CountActiveUsersAsync(Guid organizationId, CancellationToken ct = default) =>
        context.Users.CountAsync(
            u => u.OrganizationId == organizationId && u.Status == UserStatus.Active,
            ct);

    public async Task<IReadOnlyList<User>> GetAllByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await context.Users
            .Where(u => u.OrganizationId == organizationId)
            .ToListAsync(ct);
}
