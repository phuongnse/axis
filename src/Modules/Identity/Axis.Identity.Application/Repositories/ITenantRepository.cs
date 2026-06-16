using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface ITenantRepository
{
    Task AddAsync(Tenant Tenant, CancellationToken ct = default);
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySlugAsync(TenantSlug slug, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(TenantSlug slug, CancellationToken ct = default);
}
