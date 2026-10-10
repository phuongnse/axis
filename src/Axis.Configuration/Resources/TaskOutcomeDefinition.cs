namespace Axis.Configuration.Resources;

/// <summary>An outcome of a task step as written: its name, its label and the step taken when the task is completed with it.</summary>
public sealed record TaskOutcomeDefinition
{
    public required string Name { get; init; }

    public required TextReference Label { get; init; }

    public required string Next { get; init; }
}
