using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class OrganizationRepository(IdentityDbContext context) : IOrganizationRepository
{
    public async Task AddAsync(Organization organization, CancellationToken ct = default) =>
        await context.Organizations.AddAsync(organization, ct);

    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.Organizations.FirstOrDefaultAsync(x => x.Id == id, ct);
}
