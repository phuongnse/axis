using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Solutions.Infrastructure.Repositories;

internal sealed class SolutionOperationRepository(SolutionsDbContext context) : ISolutionOperationRepository
{
    public Task<SolutionInstallationOperation?> FindByIdempotencyAsync(Guid workspaceId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        context.SolutionOperations.Include(x => x.Steps).SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IdempotencyKey == idempotencyKey, cancellationToken);
    public Task<SolutionInstallationOperation?> FindByIdAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        context.SolutionOperations.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken);
    public async Task<IReadOnlyList<SolutionInstallationOperation>> ListByInstallationAsync(Guid installationId, CancellationToken cancellationToken = default) =>
        await context.SolutionOperations.AsNoTracking().Include(x => x.Steps).Where(x => x.InstallationId == installationId).OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<Guid>> ListRunnableIdsAsync(DateTimeOffset now, int maximumCount, CancellationToken cancellationToken = default) =>
        await context.SolutionOperations
            .AsNoTracking()
            .Where(x => x.Status == InstallationOperationStatus.Pending
                || x.Status == InstallationOperationStatus.Running && x.LeaseExpiresAt <= now)
            .OrderBy(x => x.UpdatedAt)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
    public async Task AddAsync(SolutionInstallationOperation operation, CancellationToken cancellationToken = default) =>
        await context.SolutionOperations.AddAsync(operation, cancellationToken);
}
