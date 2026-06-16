using System.Text.Json;
using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Handles Form steps: suspends the execution step in WAITING state and raises
/// FormStepReached so FormBuilder can create the task.
/// The step is resumed when FormBuilder raises FormTaskSubmitted (handled by FormTaskSubmittedHandler).
/// Form ID, assignee, timeout from config.
/// </summary>
public sealed class ExecuteFormStepHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow,
    ILogger<ExecuteFormStepHandler> logger)
{
    public async Task HandleAsync(ExecuteFormStepMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.tenantId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "ExecuteFormStepHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "ExecuteFormStepHandler: step {StepId} not found", message.StepId);
            return;
        }

        // Idempotency: already waiting or terminal
        if (step.IsTerminal || step.Status == StepExecutionStatus.Waiting)
        {
            logger.LogInformation(
                "ExecuteFormStepHandler: step {StepId} already in state {Status}, skipping",
                message.StepId, step.Status);
            return;
        }

        if (!TryExtractFormConfig(message.StepConfig, out Guid formId, out string? assigneeExpr, out int? timeoutHours))
        {
            logger.LogError(
                "ExecuteFormStepHandler: invalid config for step {StepId} in execution {ExecutionId}",
                message.StepId, message.ExecutionId);

            // Fail the step — config is misconfigured
            execution.FailStep(message.StepId,
                "Form step configuration is invalid: formId is required.");
            execution.Fail("Form step configuration is invalid: formId is required.");
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (ConcurrencyException)
            {
                logger.LogInformation(
                    "ExecuteFormStepHandler: concurrent config-failure detected for step {StepId} — skipping",
                    message.StepId);
            }
            return;
        }

        // Domain event consumed by FormBuilder to create the task (raises FormStepReached internally)
        execution.ReachFormStep(message.StepId, formId, assigneeExpr, timeoutHours);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogInformation(
                "ExecuteFormStepHandler: concurrent delivery detected for step {StepId} — skipping",
                message.StepId);
            return;
        }

        logger.LogInformation(
            "Form step {StepId} waiting in execution {ExecutionId}, form={FormId}",
            message.StepId, execution.Id, formId);
    }

    private static bool TryExtractFormConfig(
        IReadOnlyDictionary<string, object?>? config,
        out Guid formId,
        out string? assigneeExpr,
        out int? timeoutHours)
    {
        formId = Guid.Empty;
        assigneeExpr = null;
        timeoutHours = null;

        if (config is null) return false;

        if (!config.TryGetValue("formId", out object? formIdRaw) || formIdRaw is null) return false;

        string? formIdStr = formIdRaw is JsonElement je ? je.ToString() : formIdRaw.ToString();
        if (!Guid.TryParse(formIdStr, out formId)) return false;

        if (config.TryGetValue("assignee", out object? assigneeRaw))
            assigneeExpr = assigneeRaw?.ToString();

        if (config.TryGetValue("timeoutHours", out object? timeoutRaw) && timeoutRaw is not null)
        {
            if (timeoutRaw is JsonElement { ValueKind: JsonValueKind.Number } jtElement
                && jtElement.TryGetInt32(out int t))
                timeoutHours = t;
            else if (int.TryParse(timeoutRaw.ToString(), out int parsed))
                timeoutHours = parsed;
        }

        return formId != Guid.Empty;
    }
}
