using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveStep;

public sealed class RemoveStepHandler(
    IWorkflowRepository workflowRepo,
    IWorkflowReferenceSync referenceSync,
    IUnitOfWork uow)
    : ICommandHandler<RemoveStepCommand>
{
    public async Task<Result> Handle(RemoveStepCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.workspaceId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        try
        {
            workflow.RemoveStep(command.StepId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await referenceSync.SyncAsync(workflow, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
