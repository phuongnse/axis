using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Handlers;

internal sealed class WorkflowArchivedHandler(FormBuilderDbContext context)
{
    public async Task Handle(WorkflowArchived @event, CancellationToken ct)
    {
        List<FormWorkflowReference> refs = await context.FormWorkflowReferences
            .Where(r => r.WorkflowId == @event.WorkflowId && r.OrganizationId == @event.OrganizationId && r.IsActive)
            .ToListAsync(ct);

        if (refs.Count == 0) return;

        foreach (FormWorkflowReference r in refs)
            r.Deactivate();

        await context.SaveChangesAsync(ct);
    }
}
