using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowPublishedHandler(
    FormBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowPublishedHandler> logger)
{
    public async Task Handle(WorkflowPublished @event, CancellationToken ct)
    {
        List<FormWorkflowReference> existing = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == @event.WorkflowId && r.OrganizationId == @event.OrganizationId)
            .ToListAsync(ct);

        // Remove references no longer in the workflow
        HashSet<Guid> newFormIds = [.. @event.ReferencedFormIds];
        int removedCount = 0;
        foreach (FormWorkflowReference old in existing.Where(r => !newFormIds.Contains(r.FormId)))
        {
            context.FormWorkflowReferences.Remove(old);
            removedCount++;
        }

        // Upsert current references
        int addedCount = 0;
        foreach (Guid formId in @event.ReferencedFormIds)
        {
            FormWorkflowReference? existing_ = existing.FirstOrDefault(r => r.FormId == formId);
            if (existing_ is null)
            {
                context.FormWorkflowReferences.Add(
                    FormWorkflowReference.Create(@event.WorkflowId, formId, @event.OrganizationId));
                addedCount++;
            }
            else
                existing_.Reactivate();
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent delivery detected for workflow {WorkflowId} — skipping",
                @event.WorkflowId);
            return;
        }
        catch (UniqueConstraintException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent insert detected for workflow {WorkflowId} — skipping",
                @event.WorkflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowPublishedHandler: references synced for workflow {WorkflowId} — added {Added}, removed {Removed}",
            @event.WorkflowId, addedCount, removedCount);
    }
}
