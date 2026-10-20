namespace Axis.Scenarios.Runner;

/// <summary>
/// How one scenario ended. <see cref="Message"/> is null when it passed. <see cref="Step"/> is the
/// 0-based index of the failing step, and null for a pass or for a failure that is no step's, such as a
/// compile error. <see cref="StepType"/> is that step's type.
/// </summary>
public sealed record ScenarioResult(string Name, string File, string Database, TimeSpan Duration, int? Step, string? StepType, string? Message)
{
    public bool Passed => Message is null;

    /// <summary>The whole failure text, with the step's number and type in front of its message, or null when it passed.</summary>
    public string? Failure => Message is null ? null : Step is { } index ? $"Step {index + 1} ({StepType}): {Message}" : Message;
}
