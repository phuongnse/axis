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
        }
    }

    private async Task SyncModelReferencesAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        List<WorkflowModelReference> existing = await context.WorkflowModelReferences
            .Where(r => r.WorkflowId == workflow.Id)
            .ToListAsync(cancellationToken);

        HashSet<Guid> currentModelIds = workflow.Triggers
            .Select(t => t.TryGetEventModelId())
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        foreach (WorkflowModelReference stale in existing.Where(r => !currentModelIds.Contains(r.ModelId)))
            context.WorkflowModelReferences.Remove(stale);

        foreach (Guid modelId in currentModelIds)
        {
            if (existing.Any(r => r.ModelId == modelId))
                continue;

            context.WorkflowModelReferences.Add(
                WorkflowModelReference.Create(workflow.Id, modelId, workflow.OrganizationId));
        }
    }
}
