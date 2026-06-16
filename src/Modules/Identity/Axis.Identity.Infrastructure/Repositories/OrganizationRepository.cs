using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class OrganizationRepository(IdentityDbContext context) : IOrganizationRepository
{
    public async Task AddAsync(Organization organization, CancellationToken ct = default) =>
        await context.Organizations.AddAsync(organization, ct);

    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await context.Organizations.FindAsync([id], ct);

    public async Task<Organization?> GetBySlugAsync(OrganizationSlug slug, CancellationToken ct = default) =>
        await context.Organizations
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public async Task<bool> SlugExistsAsync(OrganizationSlug slug, CancellationToken ct = default) =>
        await context.Organizations.AnyAsync(o => o.Slug == slug, ct);
}
