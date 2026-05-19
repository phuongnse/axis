using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

internal sealed class WorkflowUnarchivedHandler(WorkflowEngineDbContext context)
{
    public async Task Handle(WorkflowUnarchived @event, CancellationToken ct)
    {
        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

        if (existing is not null)
        {
            existing.Reactivate();
            await context.SaveChangesAsync(ct);
        }
    }
}
