namespace Axis.Scenarios;

/// <summary>How one scenario ended. <see cref="Failure"/> is null when it passed.</summary>
public sealed record ScenarioResult(string Name, string File, string Database, TimeSpan Duration, string? Failure)
{
    public bool Passed => Failure is null;
}
