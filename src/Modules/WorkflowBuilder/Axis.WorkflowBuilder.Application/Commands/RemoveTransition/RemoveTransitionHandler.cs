using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveTransition;

public sealed class RemoveTransitionHandler(IWorkflowRepository workflowRepo, IUnitOfWork uow)
    : ICommandHandler<RemoveTransitionCommand>
{
    public async Task<Result> Handle(RemoveTransitionCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.OrganizationId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        workflow.RemoveTransition(command.FromStepId, command.ToStepId);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
