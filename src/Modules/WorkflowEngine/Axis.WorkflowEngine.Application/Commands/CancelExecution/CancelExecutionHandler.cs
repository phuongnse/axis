using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Application.Commands.CancelExecution;

/// <summary>Loads the execution and delegates cancellation to the aggregate.</summary>
public sealed class CancelExecutionHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow)
    : ICommandHandler<CancelExecutionCommand>
{
    public async Task<Result> Handle(CancelExecutionCommand command, CancellationToken cancellationToken)
    {
        WorkflowExecution? execution = await execRepo.GetByIdAsync(
            command.ExecutionId, command.workspaceId, cancellationToken);
        if (execution is null)
            return Result.Failure(ErrorCodes.NotFound, "Execution not found.");

        try
        {
            execution.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
