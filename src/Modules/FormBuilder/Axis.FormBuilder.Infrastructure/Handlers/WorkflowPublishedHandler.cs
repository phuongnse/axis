using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowPublishedHandler(FormBuilderDbContext context)
{
    public async Task Handle(WorkflowPublished @event, CancellationToken ct)
    {
        List<FormWorkflowReference> existing = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == @event.WorkflowId)
            .ToListAsync(ct);

        // Remove references no longer in the workflow
        HashSet<Guid> newFormIds = [.. @event.ReferencedFormIds];
        foreach (FormWorkflowReference old in existing.Where(r => !newFormIds.Contains(r.FormId)))
            context.FormWorkflowReferences.Remove(old);

        // Upsert current references
        HashSet<Guid> existingFormIds = [.. existing.Select(r => r.FormId)];
        foreach (Guid formId in @event.ReferencedFormIds)
        {
            FormWorkflowReference? ref_ = existing.FirstOrDefault(r => r.FormId == formId);
            if (ref_ is null)
                context.FormWorkflowReferences.Add(
                    FormWorkflowReference.Create(@event.WorkflowId, formId, @event.OrganizationId));
            else
                ref_.Reactivate();
        }

        await context.SaveChangesAsync(ct);
    }
}
