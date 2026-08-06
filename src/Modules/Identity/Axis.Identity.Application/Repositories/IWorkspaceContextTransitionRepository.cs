using Axis.Identity.Domain.Aggregates;
namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceContextTransitionRepository
{
    Task AddAsync(WorkspaceContextTransition transition, CancellationToken ct = default);
    Task<WorkspaceContextTransition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkspaceContextTransition?> GetForRecoveryAsync(Guid userId, string sourceCorrelation, CancellationToken ct = default);
}
