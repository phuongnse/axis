using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class TenantRepository(IdentityDbContext context) : ITenantRepository
{
    public async Task AddAsync(Tenant Tenant, CancellationToken ct = default) =>
        await context.Tenants.AddAsync(Tenant, ct);

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await context.Tenants.FindAsync([id], ct);

    public async Task<Tenant?> GetBySlugAsync(TenantSlug slug, CancellationToken ct = default) =>
        await context.Tenants
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public async Task<bool> SlugExistsAsync(TenantSlug slug, CancellationToken ct = default) =>
        await context.Tenants.AnyAsync(o => o.Slug == slug, ct);
}
