using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;

/// <summary>US-049: Loads the workflow and delegates publish logic to the aggregate.</summary>
public sealed class PublishWorkflowHandler(
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<PublishWorkflowCommand>
{
    public async Task<Result> Handle(PublishWorkflowCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.OrganizationId, cancellationToken);
        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

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
