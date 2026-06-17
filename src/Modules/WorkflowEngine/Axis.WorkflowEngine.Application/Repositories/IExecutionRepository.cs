using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Application.Repositories;

public interface IExecutionRepository
{
    Task AddAsync(WorkflowExecution execution, CancellationToken ct = default);
    Task<WorkflowExecution?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken ct = default);

    /// <summary>Loads the execution with its steps for engine write operations. Returns a tracked entity.</summary>
    Task<WorkflowExecution?> GetByIdWithStepsAsync(Guid id, Guid workspaceId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowExecution>> GetAllAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetByWorkflowAsync(Guid workflowId, Guid workspaceId, CancellationToken ct = default);

    Task<ExecutionResponse?> GetWithStepsAsync(Guid executionId, Guid workspaceId, CancellationToken ct = default);
    Task<(IReadOnlyList<ExecutionSummaryResponse> Items, int TotalCount)> GetPagedAsync(
        Guid workspaceId, int page, int pageSize, ExecutionStatus? status = null, CancellationToken ct = default);
    Task<(IReadOnlyList<ExecutionSummaryResponse> Items, int TotalCount)> GetPagedByWorkflowAsync(
        Guid workflowId, Guid workspaceId, int page, int pageSize, ExecutionStatus? status = null, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionSummaryResponse>> GetRetriesAsync(
        Guid originalExecutionId, Guid workspaceId, CancellationToken ct = default);

    Task<int> CountCreatedSinceUtcAsync(Guid workspaceId, DateTime sinceUtc, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowExecution>> GetCancellableByWorkspaceAsync(
        Guid workspaceId,
        CancellationToken ct = default);
}
