namespace Axis.Configuration.Resources;

/// <summary>The timeout of a <c>waitEvent</c> step as written: the ISO 8601 duration from the start of the wait to its end and the step taken when no event arrives in time.</summary>
public sealed record WaitTimeoutDefinition
{
    public required string After { get; init; }

    public required string Next { get; init; }
}
