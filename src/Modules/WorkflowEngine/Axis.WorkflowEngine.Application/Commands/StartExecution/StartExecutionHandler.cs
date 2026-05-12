using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using FluentValidation;

namespace Axis.WorkflowEngine.Application.Commands.StartExecution;

/// <summary>US-090: Validates workflow is active, creates Pending execution (async engine picks it up).</summary>
public sealed class StartExecutionHandler(
    IExecutionRepository execRepo,
    IWorkflowDefinitionReader workflowReader,
    IUnitOfWork uow)
    : ICommandHandler<StartExecutionCommand, Guid>
{
    public async Task<Guid> Handle(StartExecutionCommand command, CancellationToken cancellationToken)
    {
        // US-090: only Active workflows can be triggered
        if (!await workflowReader.IsActiveAsync(command.WorkflowDefinitionId, command.OrganizationId, cancellationToken))
            throw new ValidationException("This workflow cannot be triggered. Only active workflows can be executed.");

        WorkflowExecution execution = WorkflowExecution.Create(
            command.WorkflowDefinitionId,
            command.OrganizationId,
            command.TriggerType,
            command.TriggeredByUserId,
            command.Input);

        await execRepo.AddAsync(execution, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return execution.Id;
    }
}
