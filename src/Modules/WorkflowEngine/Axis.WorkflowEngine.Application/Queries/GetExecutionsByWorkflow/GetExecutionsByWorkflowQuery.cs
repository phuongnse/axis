using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Application.Queries.GetExecutionsByWorkflow;

/// <summary>Paginated execution history for a specific workflow.</summary>
public sealed record GetExecutionsByWorkflowQuery(
    Guid WorkflowDefinitionId,
    Guid tenantId,
    int Page = 1,
    int PageSize = 25,
    ExecutionStatus? Status = null) : IQuery<PagedResult<ExecutionSummaryResponse>>;
