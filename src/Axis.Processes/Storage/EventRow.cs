namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.events</c>: one occurrence of an event that a <c>raiseEvent</c> step raised.
/// Rows are append-only and kept, even when no trigger listens. Times use the database clock.
/// </summary>
public sealed class EventRow
{
    /// <summary>The event's id, a version 7 UUID.</summary>
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The event's declared name.</summary>
    public required string Event { get; init; }

    /// <summary>The release the raising instance is pinned to.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The event's entity.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>The record the occurrence names.</summary>
    public required Guid SubjectId { get; init; }

    /// <summary>The evaluated payload as JSON text, keyed by the payload fields' declared names.</summary>
    public required string Payload { get; init; }

    /// <summary>The instance that raised the event, or null when an endpoint without a process raised it.</summary>
    public Guid? ProcessInstanceId { get; init; }

    /// <summary>
    /// The user who answers for the occurrence: the calling service account of an inbound endpoint, or the
    /// raising instance's accountable user. An event trigger starts its instance with it. Null when no
    /// person answers, and for an occurrence stored before the column existed.
    /// </summary>
    public string? AccountableUser { get; init; }

    /// <summary>The start of the raising step's transaction.</summary>
    public DateTimeOffset RaisedAt { get; init; }
}
