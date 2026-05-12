using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using FluentValidation;

namespace Axis.WorkflowEngine.Application.Commands.CancelExecution;

/// <summary>US-092: Loads the execution and delegates cancellation to the aggregate.</summary>
public sealed class CancelExecutionHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow)
    : ICommandHandler<CancelExecutionCommand>
{
    public async Task Handle(CancelExecutionCommand command, CancellationToken cancellationToken)
    {
        WorkflowExecution? execution = await execRepo.GetByIdAsync(command.ExecutionId, command.OrganizationId, cancellationToken);
        if (execution is null)
            throw new ValidationException("Execution not found.");

        try
        {
            execution.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
