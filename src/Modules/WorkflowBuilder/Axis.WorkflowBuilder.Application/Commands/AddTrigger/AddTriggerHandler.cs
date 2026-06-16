using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.AddTrigger;

public sealed class AddTriggerHandler(
    IWorkflowRepository workflowRepo,
    IWorkflowReferenceSync referenceSync,
    IUnitOfWork uow)
    : ICommandHandler<AddTriggerCommand>
{
    public async Task<Result> Handle(AddTriggerCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.TeamAccountId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        try
        {
            workflow.AddTrigger(command.TriggerType, command.Config);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(ErrorCodes.Conflict,
                $"A {command.TriggerType} trigger is already configured on this workflow.");
        }

        await referenceSync.SyncAsync(workflow, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
