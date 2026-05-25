using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.ReadModels;
using Axis.WorkflowBuilder.Domain.ValueObjects;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowBuilder.Infrastructure.Services;

internal sealed class WorkflowReferenceSync(WorkflowBuilderDbContext context) : IWorkflowReferenceSync
{
    public async Task SyncAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        await SyncFormReferencesAsync(workflow, cancellationToken);
        await SyncModelReferencesAsync(workflow, cancellationToken);
    }

    private async Task SyncFormReferencesAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        List<WorkflowFormReference> existing = await context.WorkflowFormReferences
            .Where(r => r.WorkflowId == workflow.Id)
            .ToListAsync(cancellationToken);

        HashSet<Guid> currentStepIds = workflow.Steps
            .Where(s => s.Type == StepType.Form && s.TryGetFormId().HasValue)
            .Select(s => s.Id)
            .ToHashSet();

        foreach (WorkflowFormReference stale in existing.Where(r => !currentStepIds.Contains(r.StepId)))
            context.WorkflowFormReferences.Remove(stale);

        foreach (WorkflowStep step in workflow.Steps.Where(s => s.Type == StepType.Form))
        {
            Guid? formId = step.TryGetFormId();
            if (!formId.HasValue)
                continue;

            WorkflowFormReference? row = existing.FirstOrDefault(r => r.StepId == step.Id);
            if (row is null)
            {
                context.WorkflowFormReferences.Add(
                    WorkflowFormReference.Create(
                        workflow.Id, step.Id, formId.Value, workflow.OrganizationId));
            }
            else if (row.FormId != formId.Value)
                row.Retarget(formId.Value);
            else
                row.MarkHealthy();
        }
    }

    private async Task SyncModelReferencesAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        WorkflowTrigger? eventTrigger = workflow.Triggers.FirstOrDefault(t => t.Type == TriggerType.Event);
        Guid? modelId = eventTrigger?.TryGetEventModelId();

        WorkflowModelReference? existing = await context.WorkflowModelReferences
            .FirstOrDefaultAsync(r => r.WorkflowId == workflow.Id, cancellationToken);

        if (!modelId.HasValue)
        {
            if (existing is not null)
                context.WorkflowModelReferences.Remove(existing);
            return;
        }

        if (existing is null)
        {
            context.WorkflowModelReferences.Add(
                WorkflowModelReference.Create(workflow.Id, modelId.Value, workflow.OrganizationId));
        }
        else if (existing.ModelId != modelId.Value)
            existing.Retarget(modelId.Value);
        else
            existing.MarkHealthy();
    }
}
