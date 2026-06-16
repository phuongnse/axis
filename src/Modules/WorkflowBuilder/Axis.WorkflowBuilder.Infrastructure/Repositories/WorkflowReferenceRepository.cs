using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowBuilder.Infrastructure.Repositories;

internal sealed class WorkflowReferenceRepository(WorkflowBuilderDbContext context) : IWorkflowReferenceRepository
{
    public async Task<bool> HasBrokenReferencesAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        bool brokenForm = await context.WorkflowFormReferences
            .AnyAsync(r => r.WorkflowId == workflowId && r.IsBroken, cancellationToken);

        if (brokenForm)
            return true;

        return await context.WorkflowModelReferences
            .AnyAsync(r => r.WorkflowId == workflowId && r.IsBroken, cancellationToken);
    }

    public async Task<int> CountBlockingFormReferencesAsync(
        Guid formId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from reference in context.WorkflowFormReferences
            join workflow in context.WorkflowDefinitions on reference.WorkflowId equals workflow.Id
            where reference.FormId == formId
                  && reference.tenantId == tenantId
                  && !reference.IsBroken
                  && workflow.DeletedAt == null
                  && workflow.Status != WorkflowStatus.Archived
            select reference)
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetBrokenStepIdsAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        List<Guid> stepIds = await context.WorkflowFormReferences
            .Where(r => r.WorkflowId == workflowId && r.IsBroken)
            .Select(r => r.StepId)
            .ToListAsync(cancellationToken);

        return stepIds.ToHashSet();
    }

    public async Task<IReadOnlySet<Guid>> GetBrokenModelIdsAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        List<Guid> modelIds = await context.WorkflowModelReferences
            .Where(r => r.WorkflowId == workflowId && r.IsBroken)
            .Select(r => r.ModelId)
            .ToListAsync(cancellationToken);

        return modelIds.ToHashSet();
    }
}
