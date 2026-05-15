using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed class GetWorkflowHandler(IWorkflowRepository workflowRepo)
    : IQueryHandler<GetWorkflowQuery, WorkflowDetailDto?>
{
    public async Task<WorkflowDetailDto?> Handle(GetWorkflowQuery query, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            query.WorkflowId, query.OrganizationId, cancellationToken);

        if (workflow is null)
            return null;

        return new WorkflowDetailDto(
            workflow.Id,
            workflow.Name,
            workflow.Description,
            workflow.Status,
            workflow.CreatedBy,
            workflow.CreatedAt,
            workflow.UpdatedAt,
            workflow.Steps.Select(s => new WorkflowStepDto(s.Id, s.Name, s.Type, s.Config)).ToList(),
            workflow.Transitions.Select(t => new StepTransitionDto(t.FromStepId, t.ToStepId, t.Label)).ToList(),
            workflow.Triggers.Select(t => new WorkflowTriggerDto(t.Type, t.Config)).ToList());
    }
}
