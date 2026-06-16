using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Repositories;

public interface IWorkflowRepository
{
    Task AddAsync(WorkflowDefinition workflow, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(Guid workspaceId, CancellationToken ct = default);
    Task<(IReadOnlyList<WorkflowDefinition> Items, int TotalCount)> GetPagedAsync(Guid workspaceId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid workspaceId, Guid? excludeId = null, CancellationToken ct = default);

    Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}
