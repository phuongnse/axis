using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Application.Repositories;

public interface IExecutionRepository
{
    Task AddAsync(WorkflowExecution execution, CancellationToken ct = default);
    Task<WorkflowExecution?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);

    /// <summary>Loads the execution with its steps for engine write operations. Returns a tracked entity.</summary>
    Task<WorkflowExecution?> GetByIdWithStepsAsync(Guid id, Guid organizationId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowExecution>> GetAllAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetByWorkflowAsync(Guid workflowId, Guid organizationId, CancellationToken ct = default);

    Task<ExecutionResponse?> GetWithStepsAsync(Guid executionId, Guid organizationId, CancellationToken ct = default);
    Task<(IReadOnlyList<ExecutionSummaryResponse> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId, int page, int pageSize, ExecutionStatus? status = null, CancellationToken ct = default);
    Task<(IReadOnlyList<ExecutionSummaryResponse> Items, int TotalCount)> GetPagedByWorkflowAsync(
        Guid workflowId, Guid organizationId, int page, int pageSize, ExecutionStatus? status = null, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionSummaryResponse>> GetRetriesAsync(
        Guid originalExecutionId, Guid organizationId, CancellationToken ct = default);
}
