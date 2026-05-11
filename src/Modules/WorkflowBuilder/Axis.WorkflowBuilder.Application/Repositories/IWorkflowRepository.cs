using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Repositories;

public interface IWorkflowRepository
{
    Task AddAsync(WorkflowDefinition workflow, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(Guid organizationId, CancellationToken ct = default);
    Task<(IReadOnlyList<WorkflowDefinition> Items, int TotalCount)> GetPagedAsync(Guid organizationId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken ct = default);
}
