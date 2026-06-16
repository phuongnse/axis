using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Executes a Notification step via INotificationSender (fire-and-forget: completes
/// regardless of delivery outcome). isolated; idempotency on re-delivery.
/// </summary>
public sealed class ExecuteNotificationStepHandler(
    IExecutionRepository execRepo,
    INotificationSender sender,
    IStepDispatcher dispatcher,
    ILogger<ExecuteNotificationStepHandler> logger)
{
    public async Task HandleAsync(ExecuteNotificationStepMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.workspaceId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "ExecuteNotificationStepHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "ExecuteNotificationStepHandler: step {StepId} not found", message.StepId);
            return;
        }

        // At-least-once re-delivery guard: IsTerminal is the correct boundary.
        // ExecuteNextStepHandler starts the step (Running) before dispatching.
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "ExecuteNotificationStepHandler: step {StepId} already terminal, skipping", message.StepId);
            return;
        }

        // Fire-and-forget: always report step as completed regardless of delivery outcome.
        // INotificationSender.SendAsync never throws — captures failures in its return value.
        IReadOnlyDictionary<string, object?> deliveryResult = await sender.SendAsync(
            message.StepConfig, message.Context, ct);

        // Log a warning if delivery failed but still complete the step (configurable behavior is scope)
        if (deliveryResult.TryGetValue("status", out object? status)
            && "failed".Equals(status?.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Notification step {StepId} delivery failed in execution {ExecutionId}: {Details}",
                message.StepId, message.ExecutionId,
                deliveryResult.TryGetValue("error", out object? err) ? err : "unknown");
        }

        logger.LogInformation(
            "Notification step {StepId} processed in execution {ExecutionId}, dispatching completion",
            message.StepId, message.ExecutionId);

        await dispatcher.PublishAsync(new StepCompletedMessage(
            message.ExecutionId, message.StepId, message.workspaceId, deliveryResult), ct);
    }
}
