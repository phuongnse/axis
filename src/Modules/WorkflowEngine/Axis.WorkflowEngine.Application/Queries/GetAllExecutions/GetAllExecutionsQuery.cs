using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Application.Queries.GetAllExecutions;

/// <summary>Org-wide paginated execution list.</summary>
public sealed record GetAllExecutionsQuery(
    Guid OrganizationId,
    int Page = 1,
    int PageSize = 25,
    ExecutionStatus? Status = null) : IQuery<PagedResult<ExecutionSummaryResponse>>;
