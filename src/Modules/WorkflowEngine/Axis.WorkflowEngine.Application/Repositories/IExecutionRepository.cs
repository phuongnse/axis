using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Application.Repositories;

public interface IExecutionRepository
{
    Task AddAsync(WorkflowExecution execution, CancellationToken ct = default);
    Task<WorkflowExecution?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetAllAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetByWorkflowAsync(Guid workflowId, Guid organizationId, CancellationToken ct = default);
}
