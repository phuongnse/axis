using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Executes an HTTP Request step via IHttpStepExecutor.
/// US-093: isolated exception boundary; idempotency on re-delivery; structured logging.
/// </summary>
public sealed class ExecuteHttpStepHandler(
    IExecutionRepository execRepo,
    IHttpStepExecutor executor,
    IStepDispatcher dispatcher,
    ILogger<ExecuteHttpStepHandler> logger)
{
    public async Task HandleAsync(ExecuteHttpStepMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.OrganizationId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "ExecuteHttpStepHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "ExecuteHttpStepHandler: step {StepId} not found", message.StepId);
            return;
        }

        // US-093: at-least-once re-delivery — if step already completed/failed/cancelled, skip.
        // ExecuteNextStepHandler always starts the step (Running) before dispatching this message,
        // so the Running guard would block all normal executions. IsTerminal is the correct
        // idempotency boundary: concurrent duplicate delivery is a known at-least-once limitation.
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "ExecuteHttpStepHandler: step {StepId} already terminal, skipping", message.StepId);
            return;
        }

        try
        {
            IReadOnlyDictionary<string, object?> output = await executor.ExecuteAsync(
                message.StepConfig, message.Context, ct);

            logger.LogInformation(
                "HTTP step {StepId} executed successfully in execution {ExecutionId}, dispatching completion",
                message.StepId, message.ExecutionId);

            await dispatcher.PublishAsync(new StepCompletedMessage(
                message.ExecutionId, message.StepId, message.OrganizationId, output), ct);
        }
        catch (Exception ex)
        {
            // Log the full exception for diagnostics; ex.Message is not repeated as a template
            // parameter to avoid leaking sensitive payload data (URLs, tokens, response bodies).
            logger.LogError(ex,
                "HTTP step {StepId} failed in execution {ExecutionId}",
                message.StepId, message.ExecutionId);

            await dispatcher.PublishAsync(new StepFailedMessage(
                message.ExecutionId, message.StepId, message.OrganizationId,
                ex.GetType().Name), ct);
        }
    }
}
