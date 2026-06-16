using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    public async Task AddAsync(Role role, CancellationToken ct = default) =>
        await context.Roles.AddAsync(role, ct);

    public async Task<Role?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        await context.Roles
            .FirstOrDefaultAsync(r => r.Id == id && r.tenantId == tenantId, ct);

    public async Task<Role?> GetByNameAsync(string name, Guid tenantId, CancellationToken ct = default) =>
        await context.Roles
            .FirstOrDefaultAsync(r => r.Name == name && r.tenantId == tenantId, ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        await context.Roles
            .Where(r => r.tenantId == tenantId)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Role> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<Role> query = context.Roles
            .Where(r => r.tenantId == tenantId)
            .OrderBy(r => r.Name);

        int totalCount = await query.CountAsync(ct);
        List<Role> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> NameExistsAsync(string name, Guid tenantId, Guid? excludeRoleId = null, CancellationToken ct = default) =>
        await context.Roles.AnyAsync(
            r => r.Name == name
              && r.tenantId == tenantId
              && (excludeRoleId == null || r.Id != excludeRoleId.Value),
            ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(
        IEnumerable<Guid> ids, Guid tenantId, CancellationToken ct = default)
    {
        List<Guid> idList = ids.ToList();
        return await context.Roles
            .Where(r => idList.Contains(r.Id) && r.tenantId == tenantId)
            .ToListAsync(ct);
    }
}
