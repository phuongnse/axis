using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Handles step failure: marks the step as Failed, then fails the entire execution.
/// idempotency on re-delivery; structured error logging.
/// </summary>
public sealed class StepFailedHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow,
    ILogger<StepFailedHandler> logger)
{
    public async Task HandleAsync(StepFailedMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.TeamAccountId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "StepFailedHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "StepFailedHandler: step {StepId} not found in execution {ExecutionId}",
                message.StepId, message.ExecutionId);
            return;
        }

        // Idempotency: already failed (re-delivery)
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "StepFailedHandler: step {StepId} already terminal ({Status}), skipping",
                message.StepId, step.Status);
            return;
        }

        // ErrorDetails may contain external system responses — not safe to log directly.
        logger.LogWarning(
            "Step {StepId} of type {StepType} failed in execution {ExecutionId}",
            message.StepId, step.StepType, message.ExecutionId);

        execution.FailStep(message.StepId, message.ErrorDetails);
        execution.Fail(message.ErrorDetails);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            // Another instance already committed the failure for this step.
            logger.LogInformation(
                "StepFailedHandler: concurrent failure detected for step {StepId} — skipping",
                message.StepId);
            return;
        }

        logger.LogInformation(
            "StepFailedHandler: execution {ExecutionId} marked failed due to step {StepId}",
            message.ExecutionId, message.StepId);
    }
}
