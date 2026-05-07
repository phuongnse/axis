using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using FluentValidation;

namespace Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;

/// <summary>US-049: Loads the workflow and delegates publish logic to the aggregate.</summary>
public sealed class PublishWorkflowHandler(
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<PublishWorkflowCommand>
{
    public async Task Handle(PublishWorkflowCommand command, CancellationToken cancellationToken)
    {
        var workflow = await workflowRepo.GetByIdAsync(command.WorkflowId, command.OrganizationId, cancellationToken);
        if (workflow is null)
            throw new ValidationException("Workflow not found.");

        try
        {
            workflow.Publish();
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
