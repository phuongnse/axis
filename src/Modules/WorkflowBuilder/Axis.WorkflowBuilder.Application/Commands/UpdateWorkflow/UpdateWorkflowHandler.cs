using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.UpdateWorkflow;

public sealed class UpdateWorkflowHandler(IWorkflowRepository workflowRepo, IUnitOfWork uow)
    : ICommandHandler<UpdateWorkflowCommand>
{
    public async Task<Result> Handle(UpdateWorkflowCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.OrganizationId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        if (await workflowRepo.NameExistsAsync(command.Name, command.OrganizationId, command.WorkflowId, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict, $"A workflow named '{command.Name}' already exists.");

        workflow.Update(command.Name, command.Description);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
