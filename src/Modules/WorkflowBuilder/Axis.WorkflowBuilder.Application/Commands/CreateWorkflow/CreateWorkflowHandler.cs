using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;

/// <summary>US-047: Validates name uniqueness, creates workflow with Start/End nodes.</summary>
public sealed class CreateWorkflowHandler(
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateWorkflowCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateWorkflowCommand command, CancellationToken cancellationToken)
    {
        if (await workflowRepo.NameExistsAsync(command.Name, command.OrganizationId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A workflow named '{command.Name}' already exists.");

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            command.Name, command.Description, command.OrganizationId, command.CreatedBy);

        await workflowRepo.AddAsync(workflow, cancellationToken);

        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A workflow named '{command.Name}' already exists.");
        }

        return workflow.Id;
    }
}
