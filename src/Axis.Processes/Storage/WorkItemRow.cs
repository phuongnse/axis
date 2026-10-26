namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_work_items</c>: work that is ready to run, or claimed by a worker
/// until its lease expires. Completed and failed items are deleted.
/// </summary>
public sealed class WorkItemRow
{
    public required Guid Id { get; init; }

    /// <summary>The tenant the work runs for. A worker claims only items of the tenant it polls.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The queue position, set by the database when the item is queued. It only grows and never
    /// changes, so it keeps the order of queuing whatever the clock does.
    /// </summary>
    public long Seq { get; set; }

    /// <summary>Selects the handler that runs the item.</summary>
    public required string Kind { get; init; }

    /// <summary>The item is not claimed before this time.</summary>
    public required DateTimeOffset DueAt { get; init; }

    /// <summary>When the current claim ends, or <see langword="null"/> when the item was never claimed.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>The token of the current claim, or <see langword="null"/> when the item was never claimed.</summary>
    public Guid? ClaimToken { get; init; }

    /// <summary>The process instance the item runs a step or a connector call of, or <see langword="null"/> for other work.</summary>
    public Guid? ProcessInstanceId { get; init; }

    /// <summary>The connector call a <c>connector.call</c> item sends, or <see langword="null"/> for other work.</summary>
    public Guid? ConnectorCallId { get; init; }

    /// <summary>The task a <c>task.deadline</c> item expires, or <see langword="null"/> for other work.</summary>
    public Guid? TaskId { get; init; }

    /// <summary>The package call a <c>package.call</c> item runs, or <see langword="null"/> for other work.</summary>
    public Guid? PackageCallId { get; init; }

    /// <summary>The event occurrence an <c>event.deliver</c> item delivers, or <see langword="null"/> for other work.</summary>
    public Guid? EventId { get; init; }

    /// <summary>The outbox email a <c>notification.email</c> item sends, or <see langword="null"/> for other work. It has no foreign key.</summary>
    public Guid? EmailId { get; init; }

    /// <summary>The schedule trigger a <c>trigger.schedule</c> or <c>trigger.fanOut</c> item runs, or <see langword="null"/> for other work.</summary>
    public Guid? TriggerId { get; init; }

    /// <summary>The release whose trigger a <c>trigger.fanOut</c> item runs, or <see langword="null"/> for other work.</summary>
    public Guid? ReleaseId { get; init; }

    /// <summary>The occurrence a <c>trigger.fanOut</c> item runs, or <see langword="null"/> for other work.</summary>
    public DateTimeOffset? OccurrenceAt { get; init; }

    /// <summary>The last record id a <c>trigger.fanOut</c> item handled, or <see langword="null"/> before its first batch.</summary>
    public Guid? Cursor { get; init; }

    /// <summary>The entity a <c>computed.backfill</c> item recomputes, or <see langword="null"/> for other work.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// The revision of the process instance that the step which queued a <c>process.timer</c> item left,
    /// or <see langword="null"/> for other work. The item resumes the instance only while it waits at this revision.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>The trace id of the span that queued the item, or <see langword="null"/> when no span was current.</summary>
    public string? TraceId { get; init; }

    /// <summary>The span id of the span that queued the item, or <see langword="null"/> when no span was current.</summary>
    public string? SpanId { get; init; }
}
