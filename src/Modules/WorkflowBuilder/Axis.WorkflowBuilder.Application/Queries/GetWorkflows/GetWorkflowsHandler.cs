using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflows;

/// <summary>US-048: Lists all workflow definitions for an org, ordered by last modified.</summary>
public sealed class GetWorkflowsHandler(IWorkflowRepository workflowRepo)
    : IQueryHandler<GetWorkflowsQuery, PagedResult<WorkflowSummaryDto>>
{
    public async Task<PagedResult<WorkflowSummaryDto>> Handle(
        GetWorkflowsQuery query, CancellationToken cancellationToken)
    {
        int effectivePageSize = Math.Min(query.PageSize, 100);

        (IReadOnlyList<WorkflowDefinition> items, int totalCount) =
            await workflowRepo.GetPagedAsync(query.OrganizationId, query.Page, effectivePageSize, cancellationToken);

        IReadOnlyList<WorkflowSummaryDto> dtos = items
            .Select(w => new WorkflowSummaryDto(
                w.Id, w.Name, w.Description, w.Status, w.Steps.Count, w.Triggers.Count, w.UpdatedAt))
            .ToList();

        return new PagedResult<WorkflowSummaryDto>(dtos, totalCount, query.Page, effectivePageSize);
    }
}
