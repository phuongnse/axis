using System.Text.Json;
using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Application.Services.Condition;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Handlers;

/// <summary>
/// Evaluates a Condition step's branch expressions and:
/// 1. Determines the selected branch.
/// 2. Skips all ExecutionSteps reachable only from non-selected branches.
/// 3. Dispatches StepCompletedMessage so the execution can advance.
/// /067: multi-branch; first-match wins; default branch fallback.
/// </summary>
public sealed class ExecuteConditionStepHandler(
    IExecutionRepository execRepo,
    IUnitOfWork uow,
    IStepDispatcher dispatcher,
    ILogger<ExecuteConditionStepHandler> logger)
{
    public async Task HandleAsync(ExecuteConditionStepMessage message, CancellationToken ct)
    {
        WorkflowExecution? execution = await execRepo.GetByIdWithStepsAsync(
            message.ExecutionId, message.workspaceId, ct);

        if (execution is null)
        {
            logger.LogWarning(
                "ExecuteConditionStepHandler: execution {ExecutionId} not found", message.ExecutionId);
            return;
        }

        ExecutionStep? step = execution.Steps.FirstOrDefault(s => s.Id == message.StepId);
        if (step is null)
        {
            logger.LogWarning(
                "ExecuteConditionStepHandler: step {StepId} not found", message.StepId);
            return;
        }

        // At-least-once re-delivery guard: IsTerminal is the correct boundary.
        // ExecuteNextStepHandler starts the step (Running) before dispatching.
        if (step.IsTerminal)
        {
            logger.LogInformation(
                "ExecuteConditionStepHandler: step {StepId} already terminal, skipping", message.StepId);
            return;
        }

        // Extract ordered branches from config
        IReadOnlyList<IReadOnlyDictionary<string, object?>> branches = ExtractBranches(message.StepConfig);
        if (branches.Count == 0)
        {
            logger.LogWarning(
                "ExecuteConditionStepHandler: step {StepId} has no branches configured — failing execution {ExecutionId}",
                message.StepId, message.ExecutionId);
            await dispatcher.PublishAsync(new StepFailedMessage(
                message.ExecutionId, message.StepId, message.workspaceId,
                "Condition step has no branches configured."), ct);
            return;
        }

        string? selectedLabel = ConditionEvaluator.EvaluateBranches(branches, message.Context);

        if (selectedLabel is null)
        {
            logger.LogWarning(
                "ExecuteConditionStepHandler: no branch matched context for step {StepId} in execution {ExecutionId} — failing",
                message.StepId, message.ExecutionId);
            await dispatcher.PublishAsync(new StepFailedMessage(
                message.ExecutionId, message.StepId, message.workspaceId,
                "No condition branch matched and no default branch is configured."), ct);
            return;
        }

        // Find which outgoing transition is the selected branch (label match)
        Guid thisStepDefId = step.StepDefinitionId;

        List<ConditionTransition> outgoing = message.Transitions
            .Where(t => t.FromStepId == thisStepDefId)
            .ToList();

        ConditionTransition? selectedTransition = outgoing
            .FirstOrDefault(t => string.Equals(t.Label, selectedLabel, StringComparison.OrdinalIgnoreCase));

        if (selectedTransition is null)
        {
            // Fall back to default: find first unlabeled (default) transition
            selectedTransition = outgoing.FirstOrDefault(t => t.Label is null);
        }

        if (selectedTransition is null)
        {
            logger.LogWarning(
                "ExecuteConditionStepHandler: no transition for branch '{Label}' from step {StepId} — failing execution {ExecutionId}",
                selectedLabel, message.StepId, message.ExecutionId);
            await dispatcher.PublishAsync(new StepFailedMessage(
                message.ExecutionId, message.StepId, message.workspaceId,
                $"No transition found for selected branch '{selectedLabel}'."), ct);
            return;
        }

        // Skip steps reachable only from non-selected branches
        HashSet<Guid> selectedReachable = GetReachable(selectedTransition.ToStepId, message.Transitions);
        IEnumerable<ConditionTransition> rejectedTransitions = outgoing
            .Where(t => t.ToStepId != selectedTransition.ToStepId);

        foreach (ConditionTransition rejected in rejectedTransitions)
        {
            HashSet<Guid> rejectedReachable = GetReachable(rejected.ToStepId, message.Transitions);

            foreach (Guid stepDefIdToSkip in rejectedReachable.Except(selectedReachable))
            {
                ExecutionStep? stepToSkip = execution.Steps
                    .FirstOrDefault(s => s.StepDefinitionId == stepDefIdToSkip
                                     && s.Status == StepExecutionStatus.Pending);

                if (stepToSkip is not null)
                    execution.SkipStep(stepToSkip.Id, $"Branch '{rejected.Label ?? "default"}' was not selected.");
            }
        }

        Dictionary<string, object?> output = new()
        {
            ["selectedBranch"] = selectedLabel,
            ["selectedStepDefinitionId"] = selectedTransition.ToStepId.ToString()
        };

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogInformation(
                "ExecuteConditionStepHandler: concurrent delivery detected for step {StepId} — skipping",
                message.StepId);
            return;
        }

        logger.LogInformation(
            "Condition step {StepId} in execution {ExecutionId} selected branch '{Label}'",
            message.StepId, message.ExecutionId, selectedLabel);

        await dispatcher.PublishAsync(new StepCompletedMessage(
            message.ExecutionId, message.StepId, message.workspaceId, output), ct);
    }

    private static HashSet<Guid> GetReachable(Guid startId, IReadOnlyList<ConditionTransition> transitions)
    {
        HashSet<Guid> visited = [];
        Queue<Guid> queue = new();
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (ConditionTransition t in transitions.Where(t => t.FromStepId == current))
                queue.Enqueue(t.ToStepId);
        }

        return visited;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ExtractBranches(
        IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null || !config.TryGetValue("branches", out object? raw) || raw is null)
            return [];

        if (raw is IReadOnlyList<IReadOnlyDictionary<string, object?>> typed) return typed;

        if (raw is JsonElement { ValueKind: JsonValueKind.Array } je)
        {
            List<Dictionary<string, object?>>? parsed = je.Deserialize<List<Dictionary<string, object?>>>();
            return parsed?.Cast<IReadOnlyDictionary<string, object?>>().ToList() ?? [];
        }

        return [];
    }
}
