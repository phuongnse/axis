using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceTransitionCleanupStore(IdentityDbContext context)
    : IWorkspaceTransitionCleanupStore
{
    public async Task<IReadOnlyList<WorkspaceTransitionCleanupItem>> ListTerminalWithoutRedisCleanupAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        return await context.WorkspaceContextTransitions
            .AsNoTracking()
            .Where(transition =>
                transition.Status != WorkspaceContextTransitionStatus.Pending
                && transition.RedisCleanupCompletedAt == null)
            .OrderBy(transition => transition.TerminalAt)
            .ThenBy(transition => transition.Id)
            .Select(transition => new WorkspaceTransitionCleanupItem(
                transition.Id,
                transition.SourceCorrelationDigest,
                transition.TargetCorrelationDigest,
                transition.Status,
                new DateTimeOffset(transition.ExpiresAt)))
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<bool> MarkRedisCleanupCompletedAsync(
        Guid transitionId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        int updated = await context.WorkspaceContextTransitions
            .Where(transition =>
                transition.Id == transitionId
                && transition.Status != WorkspaceContextTransitionStatus.Pending
                && transition.RedisCleanupCompletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(transition => transition.RedisCleanupCompletedAt, now.UtcDateTime)
                .SetProperty(transition => transition.Revision, transition => transition.Revision + 1), ct);
        return updated == 1;
    }
}
