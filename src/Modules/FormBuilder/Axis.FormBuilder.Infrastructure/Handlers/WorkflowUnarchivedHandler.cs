using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowUnarchivedHandler(
    FormBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowUnarchivedHandler> logger)
{
    public async Task Handle(WorkflowUnarchived @event, CancellationToken ct)
    {
        List<FormWorkflowReference> refs = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == @event.WorkflowId && r.OrganizationId == @event.OrganizationId && !r.IsActive)
            .ToListAsync(ct);

        if (refs.Count == 0)
        {
            logger.LogInformation(
                "WorkflowUnarchivedHandler: no inactive references for workflow {WorkflowId} — skipping",
                @event.WorkflowId);
            return;
        }

        foreach (FormWorkflowReference r in refs)
            r.Reactivate();

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogWarning(
                "WorkflowUnarchivedHandler: concurrent delivery detected for workflow {WorkflowId} — skipping",
                @event.WorkflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowUnarchivedHandler: reactivated {Count} reference(s) for workflow {WorkflowId}",
            refs.Count, @event.WorkflowId);
    }
}
