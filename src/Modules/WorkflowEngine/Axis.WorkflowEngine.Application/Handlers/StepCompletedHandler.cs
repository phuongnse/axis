using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Handles step completion: marks the step as Completed, merges output into context,
/// persists, then dispatches ExecuteNextStepMessage to advance the execution.
/// idempotency on re-delivery.
/// </summary>
public sealed class StepCompletedHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow,
    IStepDispatcher dispatcher,
    ILogger<StepCompletedHandler> logger)
{
    public async Task HandleAsync(StepCompletedMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.TeamAccountId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "StepCompletedHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "StepCompletedHandler: step {StepId} not found in execution {ExecutionId}",
                message.StepId, message.ExecutionId);
            return;
        }

        // Idempotency: step already completed (re-delivery of message)
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "StepCompletedHandler: step {StepId} already terminal ({Status}), skipping",
                message.StepId, step.Status);
            return;
        }

        execution.CompleteStep(message.StepId, message.Output);
        execution.MergeContext(message.Output);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            // Another instance of StepCompletedHandler already committed this step's completion.
            logger.LogInformation(
                "StepCompletedHandler: concurrent completion detected for step {StepId} — skipping",
                message.StepId);
            return;
        }

        logger.LogInformation(
            "StepCompletedHandler: step {StepId} completed in execution {ExecutionId}, advancing",
            message.StepId, message.ExecutionId);

        await dispatcher.PublishAsync(new ExecuteNextStepMessage(execution.Id, execution.TeamAccountId), ct);
    }
}
