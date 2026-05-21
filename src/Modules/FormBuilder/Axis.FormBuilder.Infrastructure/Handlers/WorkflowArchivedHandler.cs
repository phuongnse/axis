using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowArchivedHandler(
    FormBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowArchivedHandler> logger)
{
    public async Task Handle(WorkflowArchived @event, CancellationToken ct)
    {
        List<FormWorkflowReference> refs = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == @event.WorkflowId && r.OrganizationId == @event.OrganizationId && r.IsActive)
            .ToListAsync(ct);

        if (refs.Count == 0)
        {
            logger.LogInformation(
                "WorkflowArchivedHandler: no active references for workflow {WorkflowId} — skipping",
                @event.WorkflowId);
            return;
        }

        foreach (FormWorkflowReference r in refs)
            r.Deactivate();

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogWarning(
                "WorkflowArchivedHandler: concurrent delivery detected for workflow {WorkflowId} — skipping",
                @event.WorkflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowArchivedHandler: deactivated {Count} reference(s) for workflow {WorkflowId}",
            refs.Count, @event.WorkflowId);
    }
}
