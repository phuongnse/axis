using axis.workflowbuilder.events;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowArchivedHandler(
    FormBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowArchivedHandler> logger)
{
    public async Task Handle(WorkflowArchivedEvent @event, CancellationToken ct)
    {
        Guid workflowId = @event.WorkflowId();
        Guid teamAccountId = @event.TeamAccountId();

        List<FormWorkflowReference> refs = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == workflowId && r.TeamAccountId == teamAccountId && r.IsActive)
            .ToListAsync(ct);

        if (refs.Count == 0)
        {
            logger.LogInformation(
                "WorkflowArchivedHandler: no active references for workflow {WorkflowId} — skipping",
                workflowId);
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
                workflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowArchivedHandler: deactivated {Count} reference(s) for workflow {WorkflowId}",
            refs.Count, workflowId);
    }
}
