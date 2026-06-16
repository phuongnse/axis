using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.ReadModels;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Orchestrates step progression within an execution.
/// Finds the next Pending step, transitions the execution to Running (if needed),
/// then dispatches the appropriate step-type handler message.
/// step isolation — each step type is its own handler.
/// </summary>
public sealed class ExecuteNextStepHandler(
    IExecutionRepository execRepo,
    IWorkflowDefinitionReader workflowReader,
    IUnitOfWork uow,
    IStepDispatcher dispatcher,
    ILogger<ExecuteNextStepHandler> logger)
{
    public async Task HandleAsync(ExecuteNextStepMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.tenantId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "ExecuteNextStepHandler: execution {ExecutionId} not found for Tenant {tenantId}",
                message.ExecutionId, message.tenantId);
            return;
        }

        // Already completed/cancelled/failed — nothing to do (idempotency on re-delivery)
        if (execution.Status is ExecutionStatus.Completed
            or ExecutionStatus.Failed
            or ExecutionStatus.Cancelled)
        {
            logger.LogWarning(
                "ExecuteNextStepHandler: execution {ExecutionId} is already terminal ({Status}), skipping",
                message.ExecutionId, execution.Status);
            return;
        }

        // Transition Pending → Running on first step dispatch
        if (execution.Status == ExecutionStatus.Pending)
            execution.Start();

        ExecutionStep? nextStep = execution.Steps
            .Where(s => s.Status == StepExecutionStatus.Pending)
            .MinBy(s => s.DisplayOrder);

        if (nextStep is null)
        {
            // All steps done — complete the execution
            execution.Complete();
            await uow.SaveChangesAsync(ct);
            logger.LogInformation(
                "Execution {ExecutionId} completed successfully", execution.Id);
            return;
        }

        // Auto-complete structural steps inline (Start/End)
        if (nextStep.StepType is StepType.Start or StepType.End)
        {
            execution.StartStep(nextStep.Id, execution.Context);
            execution.CompleteStep(nextStep.Id, new Dictionary<string, object?>());

            if (nextStep.StepType == StepType.End)
                execution.Complete();

            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (ConcurrencyException)
            {
                logger.LogInformation(
                    "Concurrent delivery detected for {StepType} step {StepId} in execution {ExecutionId} — skipping",
                    nextStep.StepType, nextStep.Id, execution.Id);
                return;
            }

            logger.LogInformation(
                "{StepType} step {StepId} auto-completed in execution {ExecutionId}",
                nextStep.StepType, nextStep.Id, execution.Id);

            if (nextStep.StepType != StepType.End)
                await dispatcher.PublishAsync(new ExecuteNextStepMessage(execution.Id, execution.tenantId), ct);

            return;
        }

        // Load snapshot for Condition step (needs transition graph for branch skipping)
        WorkflowSnapshot? snapshot = null;
        if (nextStep.StepType == StepType.Condition)
        {
            snapshot = await workflowReader.GetSnapshotAsync(
                execution.WorkflowDefinitionId, execution.tenantId, ct);
        }

        // For non-Condition steps, config comes from the snapshot (loaded at start, stored in context if needed)
        // The snapshot is available for Condition; other step types store config in the message
        StepDefinitionSnapshot? stepDef = snapshot?.Steps
            .FirstOrDefault(s => s.Id == nextStep.StepDefinitionId);

        IReadOnlyDictionary<string, object?>? config = stepDef?.Config;

        // For non-Condition steps we need config from the snapshot — load it now
        if (nextStep.StepType is not StepType.Condition && snapshot is null)
        {
            snapshot = await workflowReader.GetSnapshotAsync(
                execution.WorkflowDefinitionId, execution.tenantId, ct);
            stepDef = snapshot?.Steps.FirstOrDefault(s => s.Id == nextStep.StepDefinitionId);
            config = stepDef?.Config;
        }

        execution.StartStep(nextStep.Id, execution.Context);
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogInformation(
                "Concurrent delivery detected for {StepType} step {StepId} in execution {ExecutionId} — skipping",
                nextStep.StepType, nextStep.Id, execution.Id);
            return;
        }

        logger.LogInformation(
            "Dispatching {StepType} step {StepId} for execution {ExecutionId}",
            nextStep.StepType, nextStep.Id, execution.Id);

        await DispatchStepHandler(execution, nextStep, config, snapshot, dispatcher, ct);
    }

    private static async Task DispatchStepHandler(
        WorkflowExecution execution,
        ExecutionStep step,
        IReadOnlyDictionary<string, object?>? config,
        WorkflowSnapshot? snapshot,
        IStepDispatcher dispatcher,
        CancellationToken ct)
    {
        switch (step.StepType)
        {
            case StepType.Form:
                await dispatcher.PublishAsync(new ExecuteFormStepMessage(
                    execution.Id, step.Id, execution.tenantId,
                    execution.WorkflowDefinitionId, config, execution.Context), ct);
                break;

            case StepType.HttpRequest:
                await dispatcher.PublishAsync(new ExecuteHttpStepMessage(
                    execution.Id, step.Id, execution.tenantId, config, execution.Context), ct);
                break;

            case StepType.Condition:
                List<ConditionTransition> transitions = snapshot?.Transitions
                    .Select(t => new ConditionTransition(t.FromStepId, t.ToStepId, t.Label))
                    .ToList() ?? [];
                List<Guid> allStepDefIds = snapshot?.Steps
                    .Select(s => s.Id)
                    .ToList() ?? [];
                await dispatcher.PublishAsync(new ExecuteConditionStepMessage(
                    execution.Id, step.Id, execution.tenantId,
                    config, execution.Context, allStepDefIds, transitions), ct);
                break;

            case StepType.Script:
                await dispatcher.PublishAsync(new ExecuteScriptStepMessage(
                    execution.Id, step.Id, execution.tenantId, config, execution.Context), ct);
                break;

            case StepType.Notification:
                await dispatcher.PublishAsync(new ExecuteNotificationStepMessage(
                    execution.Id, step.Id, execution.tenantId, config, execution.Context), ct);
                break;
        }
    }
}
