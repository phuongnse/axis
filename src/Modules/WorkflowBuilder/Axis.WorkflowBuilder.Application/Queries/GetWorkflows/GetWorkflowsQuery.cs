using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflows;

/// <summary>Returns a paginated list of workflow definitions for an organization.</summary>
public sealed record GetWorkflowsQuery(Guid OrganizationId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<WorkflowSummaryDto>>;
