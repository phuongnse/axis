using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Repositories;

public interface IWorkflowRepository
{
    Task AddAsync(WorkflowDefinition workflow, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, Guid teamAccountId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(Guid teamAccountId, CancellationToken ct = default);
    Task<(IReadOnlyList<WorkflowDefinition> Items, int TotalCount)> GetPagedAsync(Guid teamAccountId, int page, int pageSize, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid teamAccountId, Guid? excludeId = null, CancellationToken ct = default);

    Task<int> CountByTeamAccountAsync(Guid teamAccountId, CancellationToken ct = default);
}
