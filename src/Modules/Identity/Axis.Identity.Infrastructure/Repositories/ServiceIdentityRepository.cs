using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class ServiceIdentityRepository(IdentityDbContext context) : IServiceIdentityRepository
{
    public Task AddAsync(ServiceIdentity identity, CancellationToken ct = default) =>
        context.ServiceIdentities.AddAsync(identity, ct).AsTask();

    public Task<ServiceIdentity?> GetAsync(Guid workspaceId, Guid identityId, CancellationToken ct = default) =>
        context.ServiceIdentities.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == identityId, ct);

    public Task<ServiceIdentity?> GetByIdAsync(Guid identityId, CancellationToken ct = default) =>
        context.ServiceIdentities.FirstOrDefaultAsync(x => x.Id == identityId, ct);

    public Task<ServiceIdentity?> GetByClientIdAsync(string clientId, CancellationToken ct = default) =>
        context.ServiceIdentities.FirstOrDefaultAsync(x => x.ClientId == clientId, ct);

    public Task<bool> ClientIdExistsAsync(string clientId, CancellationToken ct = default) =>
        context.ServiceIdentities.AnyAsync(x => x.ClientId == clientId, ct);

    public async Task<IReadOnlyList<ServiceIdentity>> ListAsync(Guid workspaceId, CancellationToken ct = default) =>
        await context.ServiceIdentities
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.ClientId)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
}
