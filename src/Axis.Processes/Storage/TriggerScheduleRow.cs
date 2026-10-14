namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.trigger_schedules</c>: the state of one schedule trigger. Activation writes it,
/// keeping the next occurrence of a trigger whose cron text and time zone did not change, and the
/// <c>trigger.schedule</c> work item moves it to the next occurrence.
/// </summary>
public sealed class TriggerScheduleRow
{
    /// <summary>The id of the trigger resource.</summary>
    public required Guid TriggerId { get; init; }

    /// <summary>The application the trigger belongs to. Activation uses it to find the triggers a new release removed.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The release whose trigger the schedule follows.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The cron text the next occurrence was computed from, or <see langword="null"/> for a row written before it was stored.</summary>
    public string? Schedule { get; init; }

    /// <summary>The time zone id the next occurrence was computed in, or <see langword="null"/> for a row written before it was stored.</summary>
    public string? TimeZone { get; init; }

    /// <summary>The next occurrence, a whole minute in UTC.</summary>
    public required DateTimeOffset NextOccurrenceAt { get; init; }

    /// <summary>The latest occurrence the worker has run, or <see langword="null"/> when none has run.</summary>
    public DateTimeOffset? LastOccurrenceAt { get; init; }
}
