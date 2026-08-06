using Axis.Identity.Domain.Aggregates;
namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceContextTransitionRepository
{
    Task AddAsync(WorkspaceContextTransition transition, CancellationToken ct = default);
    Task<WorkspaceContextTransition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkspaceContextTransition?> GetBySourceCorrelationDigestAsync(
        Guid userId,
        string sourceCorrelationDigest,
        CancellationToken ct = default);
    Task<WorkspaceContextTransition?> GetByTargetCorrelationDigestAsync(
        Guid userId,
        string targetCorrelationDigest,
        CancellationToken ct = default);
}
