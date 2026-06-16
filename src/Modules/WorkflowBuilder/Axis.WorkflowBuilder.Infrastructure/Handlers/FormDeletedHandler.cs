using axis.formbuilder.events;
using Axis.FormBuilder.Contracts;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.ReadModels;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowBuilder.Infrastructure.Handlers;

/// <summary>
/// Flags Form steps that reference a deleted form.
/// </summary>
internal sealed class FormDeletedHandler(
    WorkflowBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<FormDeletedHandler> logger)
{
    public async Task Handle(FormDeletedEvent @event, CancellationToken cancellationToken)
    {
        Guid workspaceId = @event.workspaceId();
        Guid formId = @event.FormId();

        List<WorkflowFormReference> references = await context.WorkflowFormReferences
            .Where(r => r.workspaceId == workspaceId && r.FormId == formId)
            .ToListAsync(cancellationToken);

        int flagged = 0;
        foreach (WorkflowFormReference reference in references)
        {
            if (!reference.IsBroken)
            {
                reference.MarkBroken();
                flagged++;
            }
        }

        if (flagged > 0)
            await uow.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "FormDeletedHandler: flagged {Count} workflow form reference(s) for deleted form {FormId} Workspace {workspaceId}",
            flagged, formId, workspaceId);
    }
}
