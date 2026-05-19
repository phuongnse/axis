using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

internal sealed class WorkflowPublishedHandler(WorkflowEngineDbContext context)
{
    public async Task Handle(WorkflowPublished @event, CancellationToken ct)
    {
        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

        if (existing is null)
            context.WorkflowActiveStatuses.Add(
                WorkflowActiveStatus.Activated(@event.WorkflowId, @event.OrganizationId));
        else
            existing.Reactivate();

        await context.SaveChangesAsync(ct);
    }
}
