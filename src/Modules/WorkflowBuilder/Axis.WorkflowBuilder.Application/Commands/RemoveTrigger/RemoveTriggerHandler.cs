using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveTrigger;

public sealed class RemoveTriggerHandler(IWorkflowRepository workflowRepo, IUnitOfWork uow)
    : ICommandHandler<RemoveTriggerCommand>
{
    public async Task<Result> Handle(RemoveTriggerCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.OrganizationId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        workflow.RemoveTrigger(command.TriggerType);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
