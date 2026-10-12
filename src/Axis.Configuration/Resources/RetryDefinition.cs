namespace Axis.Configuration.Resources;

/// <summary>
/// The retry policy of a <c>callConnector</c> step as written. The JSON Schema keeps
/// <see cref="Attempts"/> from 1 to 10. Whether <see cref="Backoff"/> is a valid duration is
/// checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record RetryDefinition
{
    /// <summary>The number of attempts, the first one included, or null for the default of 3.</summary>
    public int? Attempts { get; init; }

    /// <summary>The ISO 8601 duration before the second attempt, or null for the default of 30 seconds.</summary>
    public string? Backoff { get; init; }
}
