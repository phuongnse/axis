using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflows;

/// <summary>US-048: Lists all workflows for an org, ordered by last modified.</summary>
public sealed class GetWorkflowsHandler(IWorkflowRepository workflowRepo)
    : IQueryHandler<GetWorkflowsQuery, IReadOnlyList<WorkflowSummaryDto>>
{
    public async Task<IReadOnlyList<WorkflowSummaryDto>> Handle(
        GetWorkflowsQuery query, CancellationToken cancellationToken)
    {
        var workflows = await workflowRepo.GetAllAsync(query.OrganizationId, cancellationToken);

        return workflows
            .OrderByDescending(w => w.UpdatedAt)
            .Select(w => new WorkflowSummaryDto(
                w.Id,
                w.Name,
                w.Description,
                w.Status,
                w.Steps.Count,
                w.Triggers.Count,
                w.UpdatedAt))
            .ToList()
            .AsReadOnly();
    }
}
