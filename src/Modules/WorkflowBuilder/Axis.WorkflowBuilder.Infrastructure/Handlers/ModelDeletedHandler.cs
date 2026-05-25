using axis.datamodeling.events;
using Axis.DataModeling.Contracts;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.ReadModels;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowBuilder.Infrastructure.Handlers;

/// <summary>
/// Flags Event triggers that reference a deleted model (E03 US-033, E04 US-065).
/// </summary>
internal sealed class ModelDeletedHandler(
    WorkflowBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<ModelDeletedHandler> logger)
{
    public async Task Handle(ModelDeletedEvent @event, CancellationToken cancellationToken)
    {
        Guid organizationId = @event.OrganizationId();
        Guid modelId = @event.ModelId();

        List<WorkflowModelReference> references = await context.WorkflowModelReferences
            .Where(r => r.OrganizationId == organizationId && r.ModelId == modelId)
            .ToListAsync(cancellationToken);

        int flagged = 0;
        foreach (WorkflowModelReference reference in references)
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
            "ModelDeletedHandler: flagged {Count} workflow model reference(s) for deleted model {ModelId} org {OrganizationId}",
            flagged, modelId, organizationId);
    }
}
