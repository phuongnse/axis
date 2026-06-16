using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IOrganizationRepository
{
    Task AddAsync(Organization organization, CancellationToken ct = default);
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Organization?> GetBySlugAsync(OrganizationSlug slug, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(OrganizationSlug slug, CancellationToken ct = default);
}
