using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.UnarchiveWorkflow;

public sealed class UnarchiveWorkflowHandler(IWorkflowRepository workflowRepo, IUnitOfWork uow)
    : ICommandHandler<UnarchiveWorkflowCommand>
{
    public async Task<Result> Handle(UnarchiveWorkflowCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.TeamAccountId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        try
        {
            workflow.Unarchive();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
