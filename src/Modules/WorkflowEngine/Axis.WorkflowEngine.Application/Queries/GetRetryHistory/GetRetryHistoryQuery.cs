using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;

namespace Axis.WorkflowEngine.Application.Queries.GetRetryHistory;

/// <summary>US-101: Returns all retry executions linked to an original execution, in chronological order.</summary>
public sealed record GetRetryHistoryQuery(
    Guid OriginalExecutionId,
    Guid OrganizationId) : IQuery<IReadOnlyList<ExecutionSummaryResponse>>;
