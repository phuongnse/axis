using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;

namespace Axis.WorkflowEngine.Application.Queries.GetRetryHistory;

/// <summary>Returns all retry executions linked to an original execution, in chronological order.</summary>
public sealed record GetRetryHistoryQuery(
    Guid OriginalExecutionId,
    Guid tenantId) : IQuery<IReadOnlyList<ExecutionSummaryResponse>>;
