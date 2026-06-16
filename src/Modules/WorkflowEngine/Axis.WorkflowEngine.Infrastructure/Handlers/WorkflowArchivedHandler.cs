using axis.workflowbuilder.events;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Contracts;
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
    public async Task Handle(WorkflowArchivedEvent @event, CancellationToken ct)
    {
        Guid workflowId = @event.WorkflowId();
        Guid organizationId = @event.OrganizationId();

        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId
                                   && w.OrganizationId == organizationId, ct);

        if (existing is null)
        {
            logger.LogInformation(
                "WorkflowArchivedHandler: active status for workflow {WorkflowId} not found — skipping",
                workflowId);
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
                workflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowArchivedHandler: workflow {WorkflowId} deactivated for org {OrganizationId}",
            workflowId, organizationId);
    }
}
