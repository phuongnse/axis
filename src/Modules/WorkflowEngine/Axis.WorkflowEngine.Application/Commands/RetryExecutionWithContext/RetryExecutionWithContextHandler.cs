using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Application.Commands.RetryExecutionWithContext;

public sealed class RetryExecutionWithContextHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryExecutionWithContextCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RetryExecutionWithContextCommand command, CancellationToken cancellationToken)
    {
        WorkflowExecution? original = await execRepo.GetByIdAsync(
            command.ExecutionId, command.workspaceId, cancellationToken);
        if (original is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Execution not found.");

        try
        {
            WorkflowExecution retry = original.CreateRetryWithModifiedContext(
                command.RetriedByUserId, command.ModifiedContext);
            await execRepo.AddAsync(retry, cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
            return retry.Id;
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, ex.Message);
        }
    }
}
