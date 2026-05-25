using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Queries.GetWorkflow;

public sealed class GetWorkflowHandler(
    IWorkflowRepository workflowRepo,
    IWorkflowReferenceRepository referenceRepo)
    : IQueryHandler<GetWorkflowQuery, WorkflowDetailDto?>
{
    public async Task<WorkflowDetailDto?> Handle(GetWorkflowQuery query, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            query.WorkflowId, query.OrganizationId, cancellationToken);

        if (workflow is null)
            return null;

        IReadOnlySet<Guid> brokenStepIds = await referenceRepo.GetBrokenStepIdsAsync(
            workflow.Id, cancellationToken);
        bool brokenEventTrigger = await referenceRepo.HasBrokenEventTriggerAsync(
            workflow.Id, cancellationToken);

        return new WorkflowDetailDto(
            workflow.Id,
            workflow.Name,
            workflow.Description,
            workflow.Status,
            workflow.CreatedBy,
            workflow.CreatedAt,
            workflow.UpdatedAt,
            workflow.Steps
                .Select(s => new WorkflowStepDto(
                    s.Id, s.Name, s.Type, s.Config, brokenStepIds.Contains(s.Id)))
                .ToList(),
            workflow.Transitions.Select(t => new StepTransitionDto(t.FromStepId, t.ToStepId, t.Label)).ToList(),
            workflow.Triggers
                .Select(t => new WorkflowTriggerDto(
                    t.Type,
                    t.Config,
                    t.Type == TriggerType.Event && brokenEventTrigger))
                .ToList());
    }
}
