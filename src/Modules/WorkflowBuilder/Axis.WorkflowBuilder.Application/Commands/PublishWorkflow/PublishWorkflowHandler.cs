using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;

/// <summary>Loads the workflow and delegates publish logic to the aggregate.</summary>
public sealed class PublishWorkflowHandler(
    IWorkflowRepository workflowRepo,
    IWorkflowReferenceSync referenceSync,
    IUnitOfWork uow)
    : ICommandHandler<PublishWorkflowCommand>
{
    public async Task<Result> Handle(PublishWorkflowCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.workspaceId, cancellationToken);
        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        WorkflowReferenceSyncResult syncResult =
            await referenceSync.SyncAsync(workflow, cancellationToken);

        if (syncResult.HasBrokenReferences)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Workflow has broken references (deleted form or model). Fix them before publishing.");
        }

        try
        {
            workflow.Publish();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
