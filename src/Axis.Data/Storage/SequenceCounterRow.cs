namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.sequence_counters</c>: the last number a sequence handed out in a period.
/// </summary>
public sealed class SequenceCounterRow
{
    /// <summary>The <c>id</c> of the <c>sequence</c> resource.</summary>
    public required Guid SequenceId { get; init; }

    /// <summary>The <c>id</c> of the application manifest that declares the sequence.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The UTC year, or 0 when the sequence's format has no <c>{yyyy}</c>.</summary>
    public required int Period { get; init; }

    /// <summary>The last number handed out.</summary>
    public required long LastValue { get; init; }
}
