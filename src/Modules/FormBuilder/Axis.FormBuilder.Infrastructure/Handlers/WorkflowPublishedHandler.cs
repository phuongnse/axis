using axis.workflowbuilder.events;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowPublishedHandler(
    FormBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowPublishedHandler> logger)
{
    public async Task Handle(WorkflowPublishedEvent @event, CancellationToken ct)
    {
        Guid workflowId = @event.WorkflowId();
        Guid organizationId = @event.OrganizationId();
        IReadOnlyList<Guid> referencedFormIds = @event.ReferencedFormIds();

        List<FormWorkflowReference> existing = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == workflowId && r.OrganizationId == organizationId)
            .ToListAsync(ct);

        HashSet<Guid> newFormIds = [.. referencedFormIds];
        int removedCount = 0;
        foreach (FormWorkflowReference old in existing.Where(r => !newFormIds.Contains(r.FormId)))
        {
            context.FormWorkflowReferences.Remove(old);
            removedCount++;
        }

        int addedCount = 0;
        foreach (Guid formId in referencedFormIds)
        {
            FormWorkflowReference? existingRef = existing.FirstOrDefault(r => r.FormId == formId);
            if (existingRef is null)
            {
                context.FormWorkflowReferences.Add(
                    FormWorkflowReference.Create(workflowId, formId, organizationId));
                addedCount++;
            }
            else
                existingRef.Reactivate();
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent delivery detected for workflow {WorkflowId} — skipping",
                workflowId);
            return;
        }
        catch (UniqueConstraintException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent insert detected for workflow {WorkflowId} — skipping",
                workflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowPublishedHandler: references synced for workflow {WorkflowId} — added {Added}, removed {Removed}",
            workflowId, addedCount, removedCount);
    }
}
