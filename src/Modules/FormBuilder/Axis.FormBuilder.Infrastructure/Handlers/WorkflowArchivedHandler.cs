using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowArchivedHandler(FormBuilderDbContext context)
{
    public async Task Handle(WorkflowArchived @event, CancellationToken ct)
    {
        List<Domain.ReadModels.FormWorkflowReference> refs = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == @event.WorkflowId && r.IsActive)
            .ToListAsync(ct);

        if (refs.Count == 0) return;

        foreach (Domain.ReadModels.FormWorkflowReference r in refs)
            r.Deactivate();

        await context.SaveChangesAsync(ct);
    }
}
