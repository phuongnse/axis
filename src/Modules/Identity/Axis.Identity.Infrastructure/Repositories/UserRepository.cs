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

    public async Task<bool> EmailExistsPlatformWideAsync(Email email, CancellationToken ct = default) =>
        await context.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<User?> FindByEmailGloballyAsync(Email email, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User?> GetByIdPlatformWideAsync(Guid id, CancellationToken ct = default) =>
        await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

}
