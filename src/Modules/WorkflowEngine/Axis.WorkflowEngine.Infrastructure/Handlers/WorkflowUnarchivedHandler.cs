using axis.workflowbuilder.events;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Contracts;
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
    public async Task Handle(WorkflowUnarchivedEvent @event, CancellationToken ct)
    {
        Guid workflowId = @event.WorkflowId();
        Guid tenantId = @event.tenantId();

        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId
                                   && w.tenantId == tenantId, ct);

        if (existing is null)
        {
            logger.LogInformation(
                "WorkflowUnarchivedHandler: active status for workflow {WorkflowId} not found — skipping",
                workflowId);
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
                workflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowUnarchivedHandler: workflow {WorkflowId} reactivated",
            workflowId);
    }
}
