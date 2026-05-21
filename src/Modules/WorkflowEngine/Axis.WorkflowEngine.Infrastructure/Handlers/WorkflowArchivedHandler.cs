using Axis.Shared.Application;
using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

internal sealed class WorkflowArchivedHandler(
    WorkflowEngineDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowArchivedHandler> logger)
{
    public async Task Handle(WorkflowArchived @event, CancellationToken ct)
    {
        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

        if (existing is null)
        {
            logger.LogInformation(
                "WorkflowArchivedHandler: active status for workflow {WorkflowId} not found — skipping",
                @event.WorkflowId);
            return;
        }

        existing.Deactivate();

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
            "WorkflowArchivedHandler: workflow {WorkflowId} deactivated for org {OrganizationId}",
            @event.WorkflowId, @event.OrganizationId);
    }
}
