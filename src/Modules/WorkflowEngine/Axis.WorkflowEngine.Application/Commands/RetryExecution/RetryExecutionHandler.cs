using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using FluentValidation;

namespace Axis.WorkflowEngine.Application.Commands.RetryExecution;

/// <summary>US-100: Loads a failed execution and creates a linked retry execution.</summary>
public sealed class RetryExecutionHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow)
    : ICommandHandler<RetryExecutionCommand, Guid>
{
    public async Task<Guid> Handle(RetryExecutionCommand command, CancellationToken cancellationToken)
    {
        var original = await execRepo.GetByIdAsync(command.ExecutionId, command.OrganizationId, cancellationToken);
        if (original is null)
            throw new ValidationException("Execution not found.");

        try
        {
            var retry = original.CreateRetry(command.RetriedByUserId);
            await execRepo.AddAsync(retry, cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
            return retry.Id;
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }
}
