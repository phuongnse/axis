using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Repositories;

internal sealed class SolutionInstallationRepository(SolutionsDbContext context) : ISolutionInstallationRepository
{
    public Task<SolutionInstallation?> FindBySolutionKeyAsync(Guid workspaceId, string solutionKey, CancellationToken cancellationToken = default) =>
        context.SolutionInstallations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.SolutionKey == solutionKey, cancellationToken);
    public Task<SolutionInstallation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SolutionInstallations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<SolutionInstallation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        await context.SolutionInstallations.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    public async Task AddAsync(SolutionInstallation installation, CancellationToken cancellationToken = default) =>
        await context.SolutionInstallations.AddAsync(installation, cancellationToken);
    public async Task<IReadOnlyList<SolutionInstallation>> ListByPublisherKeyAsync(string publisherId, string keyId, CancellationToken cancellationToken = default) =>
        await (from installation in context.SolutionInstallations
               join version in context.SolutionVersions on installation.SolutionVersionId equals version.Id
               where version.PublisherId == publisherId && version.PublisherKeyId == keyId
               select installation).ToListAsync(cancellationToken);
}
