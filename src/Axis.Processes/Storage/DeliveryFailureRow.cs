namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.delivery_failures</c>: the failures counted for one delivery target. The kind
/// and the target key it, so each target of each work item kind has one count.
/// </summary>
public sealed class DeliveryFailureRow
{
    /// <summary>The work item kind, such as <c>event.deliver</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// The target as text. For an <c>async</c> trigger it is the trigger id and the event id, joined by
    /// <c>/</c>.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>The failures counted.</summary>
    public required int Attempts { get; init; }

    /// <summary>The type and SQLSTATE of the last failure. It holds no exception message.</summary>
    public required string LastError { get; init; }

    /// <summary>The time of the last failure.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
