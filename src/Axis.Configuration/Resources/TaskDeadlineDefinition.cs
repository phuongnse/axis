namespace Axis.Configuration.Resources;

/// <summary>The deadline of a task step as written: the ISO 8601 duration from the task's creation to its expiry and the step taken when it expires.</summary>
public sealed record TaskDeadlineDefinition
{
    public required string After { get; init; }

    public required string Next { get; init; }
}
