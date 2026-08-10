namespace Axis.Identity.Application.Services;

public interface IWorkspaceTransitionExpiryStore
{
    Task<IReadOnlyList<WorkspaceTransitionExpiryItem>> ListExpiredPendingAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken ct = default);
}

public sealed record WorkspaceTransitionExpiryItem(
    Guid TransitionId,
    Guid UserId,
    string SourceCorrelationDigest);
