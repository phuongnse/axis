using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceContextTransitionRepository(IdentityDbContext context)
    : IWorkspaceContextTransitionRepository
{
    public async Task AddAsync(WorkspaceContextTransition transition, CancellationToken ct = default) =>
        await context.WorkspaceContextTransitions.AddAsync(transition, ct);

    public Task<WorkspaceContextTransition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.WorkspaceContextTransitions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<WorkspaceContextTransition?> GetForRecoveryAsync(
        Guid userId,
        string sourceCorrelation,
        CancellationToken ct = default) =>
        context.WorkspaceContextTransitions.FirstOrDefaultAsync(
            x => x.UserId == userId && x.SourceCorrelation == sourceCorrelation.Trim(), ct);
}
