using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Executes a Script step via IScriptExecutor (sandboxed JavaScript).
/// isolated exception boundary; idempotency on re-delivery.
/// </summary>
public sealed class ExecuteScriptStepHandler(
    IExecutionRepository execRepo,
    IScriptExecutor executor,
    IStepDispatcher dispatcher,
    ILogger<ExecuteScriptStepHandler> logger)
{
    public async Task HandleAsync(ExecuteScriptStepMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.tenantId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "ExecuteScriptStepHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "ExecuteScriptStepHandler: step {StepId} not found", message.StepId);
            return;
        }

        // At-least-once re-delivery guard: IsTerminal is the correct boundary.
        // ExecuteNextStepHandler starts the step (Running) before dispatching, so Running is
        // the expected state on first delivery — not a signal to skip.
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "ExecuteScriptStepHandler: step {StepId} already terminal, skipping", message.StepId);
            return;
        }

        try
        {
            IReadOnlyDictionary<string, object?> output = await executor.ExecuteAsync(
                message.StepConfig, message.Context, ct);

            logger.LogInformation(
                "Script step {StepId} executed successfully in execution {ExecutionId}, dispatching completion",
                message.StepId, message.ExecutionId);

            await dispatcher.PublishAsync(new StepCompletedMessage(
                message.ExecutionId, message.StepId, message.tenantId, output), ct);
        }
        catch (Exception ex)
        {
            // Log the full exception for diagnostics; ex.Message is not repeated as a template
            // parameter to avoid leaking sensitive script output or execution context data.
            logger.LogError(ex,
                "Script step {StepId} failed in execution {ExecutionId}",
                message.StepId, message.ExecutionId);

            await dispatcher.PublishAsync(new StepFailedMessage(
                message.ExecutionId, message.StepId, message.tenantId,
                ex.GetType().Name), ct);
        }
    }
}
