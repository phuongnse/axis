using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using FluentValidation;

namespace Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;

/// <summary>US-047: Validates name uniqueness, creates workflow with Start/End nodes.</summary>
public sealed class CreateWorkflowHandler(
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateWorkflowCommand, Guid>
{
    public async Task<Guid> Handle(CreateWorkflowCommand command, CancellationToken cancellationToken)
    {
        if (await workflowRepo.NameExistsAsync(command.Name, command.OrganizationId, null, cancellationToken))
            throw new ValidationException($"A workflow named '{command.Name}' already exists.");

        WorkflowDefinition workflow = WorkflowDefinition.Create(command.Name, command.Description, command.OrganizationId, command.CreatedBy);

        await workflowRepo.AddAsync(workflow, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return workflow.Id;
    }
}
