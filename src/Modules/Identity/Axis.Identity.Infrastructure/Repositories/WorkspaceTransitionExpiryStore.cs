using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceTransitionExpiryStore(IdentityDbContext context)
    : IWorkspaceTransitionExpiryStore
{
    public async Task<IReadOnlyList<WorkspaceTransitionExpiryItem>> ListExpiredPendingAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken ct = default)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        return await context.WorkspaceContextTransitions
            .AsNoTracking()
            .Where(transition =>
                transition.Status == WorkspaceContextTransitionStatus.Pending
                && transition.ExpiresAt < now.UtcDateTime)
            .OrderBy(transition => transition.ExpiresAt)
            .ThenBy(transition => transition.Id)
            .Select(transition => new WorkspaceTransitionExpiryItem(
                transition.Id,
                transition.UserId,
                transition.SourceCorrelationDigest))
            .Take(batchSize)
            .ToListAsync(ct);
    }
}
