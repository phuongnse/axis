using Axis.Shared.Application;
using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

internal sealed class WorkflowUnarchivedHandler(
    WorkflowEngineDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowUnarchivedHandler> logger)
{
    public async Task Handle(WorkflowUnarchived @event, CancellationToken ct)
    {
        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

        if (existing is null)
        {
            logger.LogInformation(
                "WorkflowUnarchivedHandler: active status for workflow {WorkflowId} not found — skipping",
                @event.WorkflowId);
            return;
        }

        existing.Reactivate();

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
            "WorkflowUnarchivedHandler: workflow {WorkflowId} reactivated",
            @event.WorkflowId);
    }
}
