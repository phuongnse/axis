using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    public async Task AddAsync(Role role, CancellationToken ct = default) =>
        await context.Roles.AddAsync(role, ct);

    public async Task<Role?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default) =>
        await context.Roles
            .FirstOrDefaultAsync(r => r.Id == id && r.TeamAccountId == teamAccountId, ct);

    public async Task<Role?> GetByNameAsync(string name, Guid teamAccountId, CancellationToken ct = default) =>
        await context.Roles
            .FirstOrDefaultAsync(r => r.Name == name && r.TeamAccountId == teamAccountId, ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(Guid teamAccountId, CancellationToken ct = default) =>
        await context.Roles
            .Where(r => r.TeamAccountId == teamAccountId)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Role> Items, int TotalCount)> GetPagedAsync(
        Guid teamAccountId, int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<Role> query = context.Roles
            .Where(r => r.TeamAccountId == teamAccountId)
            .OrderBy(r => r.Name);

        int totalCount = await query.CountAsync(ct);
        List<Role> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> NameExistsAsync(string name, Guid teamAccountId, Guid? excludeRoleId = null, CancellationToken ct = default) =>
        await context.Roles.AnyAsync(
            r => r.Name == name
              && r.TeamAccountId == teamAccountId
              && (excludeRoleId == null || r.Id != excludeRoleId.Value),
            ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(
        IEnumerable<Guid> ids, Guid teamAccountId, CancellationToken ct = default)
    {
        List<Guid> idList = ids.ToList();
        return await context.Roles
            .Where(r => idList.Contains(r.Id) && r.TeamAccountId == teamAccountId)
            .ToListAsync(ct);
    }
}
