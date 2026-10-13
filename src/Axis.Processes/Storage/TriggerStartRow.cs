namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.trigger_starts</c>: the receipt of one trigger's start for one cause and
/// record. Its key, the trigger, the source and the record, makes each start happen at most once.
/// </summary>
public sealed class TriggerStartRow
{
    /// <summary>The id of the trigger resource.</summary>
    public required Guid TriggerId { get; init; }

    /// <summary>The cause: the event id for an event trigger.</summary>
    public required string Source { get; init; }

    /// <summary>The record the start is for.</summary>
    public required Guid SubjectId { get; init; }

    /// <summary><c>started</c> or <c>skipped</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>The instance the trigger started, or <see langword="null"/> when it skipped the start.</summary>
    public Guid? ProcessInstanceId { get; init; }

    /// <summary>The start of the delivery's transaction.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
