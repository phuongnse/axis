using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;

namespace Axis.WorkflowEngine.Application.Commands.RetryExecution;

/// <summary>Loads a failed execution and creates a linked retry execution.</summary>
public sealed class RetryExecutionHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryExecutionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RetryExecutionCommand command, CancellationToken cancellationToken)
    {
        WorkflowExecution? original = await execRepo.GetByIdAsync(
            command.ExecutionId, command.OrganizationId, cancellationToken);
        if (original is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Execution not found.");

        try
        {
            WorkflowExecution retry = original.CreateRetry(command.RetriedByUserId);
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
