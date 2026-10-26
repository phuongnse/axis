namespace Axis.Configuration.Resources;

/// <summary>An escalation of a task step as written: the ISO 8601 duration from the task's creation to when it runs, and what it does, <see cref="Reassign"/> the task, <see cref="Notify"/> people, or both.</summary>
public sealed record TaskEscalationDefinition
{
    public required string After { get; init; }

    public TaskAssigneeDefinition? Reassign { get; init; }

    public EscalationNotifyDefinition? Notify { get; init; }
}
