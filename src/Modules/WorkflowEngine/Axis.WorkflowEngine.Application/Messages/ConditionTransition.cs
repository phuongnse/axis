namespace Axis.WorkflowEngine.Application.Messages;

/// <summary>A single transition in the condition graph, used to determine which branch to skip.</summary>
public sealed record ConditionTransition(Guid FromStepId, Guid ToStepId, string? Label);
