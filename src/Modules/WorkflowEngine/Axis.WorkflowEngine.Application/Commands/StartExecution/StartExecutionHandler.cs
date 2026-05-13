using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Application.Commands.StartExecution;

/// <summary>US-090: Validates workflow is active, creates Pending execution (async engine picks it up).</summary>
public sealed class StartExecutionHandler(
    IExecutionRepository execRepo,
    IWorkflowDefinitionReader workflowReader,
    IUnitOfWork uow)
    : ICommandHandler<StartExecutionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(StartExecutionCommand command, CancellationToken cancellationToken)
    {
        // US-090: only Active workflows can be triggered
        if (!await workflowReader.IsActiveAsync(command.WorkflowDefinitionId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.BusinessRule,
                "This workflow cannot be triggered. Only active workflows can be executed.");

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
