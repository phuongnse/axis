namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_event_matches</c>: one use of an event occurrence by a <c>waitEvent</c>
/// step of a process instance. Its key, the instance and the occurrence, makes an instance use an
/// occurrence at most once.
/// </summary>
public sealed class ProcessEventMatchRow
{
    public required Guid ProcessInstanceId { get; init; }

    /// <summary>The occurrence the step went on with.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The <c>waitEvent</c> step that used the occurrence.</summary>
    public required string Step { get; init; }

    /// <summary>The start of the step's transaction.</summary>
    public DateTimeOffset MatchedAt { get; init; }
}
