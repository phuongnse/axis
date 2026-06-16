using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveTrigger;

public sealed class RemoveTriggerHandler(
    IWorkflowRepository workflowRepo,
    IWorkflowReferenceSync referenceSync,
    IUnitOfWork uow)
    : ICommandHandler<RemoveTriggerCommand>
{
    public async Task<Result> Handle(RemoveTriggerCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.workspaceId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        workflow.RemoveTrigger(command.TriggerType);
        await referenceSync.SyncAsync(workflow, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
