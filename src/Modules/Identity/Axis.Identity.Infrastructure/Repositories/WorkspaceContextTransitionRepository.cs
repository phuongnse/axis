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

    public Task<WorkspaceContextTransition?> GetBySourceCorrelationDigestAsync(
        Guid userId,
        string sourceCorrelationDigest,
        CancellationToken ct = default) =>
        context.WorkspaceContextTransitions.FirstOrDefaultAsync(
            x => x.UserId == userId && x.SourceCorrelationDigest == sourceCorrelationDigest, ct);

    public Task<WorkspaceContextTransition?> GetByTargetCorrelationDigestAsync(
        Guid userId,
        string targetCorrelationDigest,
        CancellationToken ct = default) =>
        context.WorkspaceContextTransitions.FirstOrDefaultAsync(
            x => x.UserId == userId && x.TargetCorrelationDigest == targetCorrelationDigest, ct);
}
