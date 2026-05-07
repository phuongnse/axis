using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Domain.ValueObjects;

/// <summary>Defines how and when a workflow execution is started.</summary>
public sealed record WorkflowTrigger(
    TriggerType Type,
    IReadOnlyDictionary<string, object?>? Config);
