namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.dead_letters</c>: a delivery that failed <c>Worker:DeliveryAttempts</c> times and
/// was set aside for an operator.
/// </summary>
public sealed class DeadLetterRow
{
    /// <summary>A version 7 UUID.</summary>
    public required Guid Id { get; init; }

    /// <summary>The work item kind, such as <c>event.deliver</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The application.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>
    /// The process whose grants govern the dead letter, or null for a webhook delivery of an
    /// occurrence that no instance raised.
    /// </summary>
    public Guid? ProcessId { get; init; }

    /// <summary>The event that failed to deliver, when the kind has one.</summary>
    public Guid? EventId { get; init; }

    /// <summary>The trigger that failed to start, when the kind has one.</summary>
    public Guid? TriggerId { get; init; }

    /// <summary>The waiting instance that failed to wake, when the kind has one.</summary>
    public Guid? ProcessInstanceId { get; init; }

    /// <summary>The email that failed to send, when the kind has one.</summary>
    public Guid? EmailId { get; init; }

    /// <summary>The webhook that failed to receive the event, when the kind has one.</summary>
    public Guid? WebhookId { get; init; }

    /// <summary>The schedule occurrence, for a fan-out.</summary>
    public DateTimeOffset? OccurrenceAt { get; init; }

    /// <summary>The failures counted.</summary>
    public required int Attempts { get; init; }

    /// <summary>The type and SQLSTATE of the last failure. It holds no exception message.</summary>
    public required string LastError { get; init; }

    /// <summary><c>open</c>, <c>redelivered</c> or <c>discarded</c>.</summary>
    public required string State { get; init; }

    /// <summary>The operator's reason for settling it.</summary>
    public string? Reason { get; init; }

    /// <summary>The user who redelivered or discarded it.</summary>
    public string? SettledBy { get; init; }

    /// <summary>The start of the transaction that wrote it.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it was redelivered or discarded.</summary>
    public DateTimeOffset? SettledAt { get; init; }
}
