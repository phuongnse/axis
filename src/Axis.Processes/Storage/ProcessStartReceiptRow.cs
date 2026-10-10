namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_start_receipts</c>: the stored <c>201</c> response of a start that
/// carried an <c>Idempotency-Key</c>. A key is unique per application and process.
/// </summary>
public sealed class ProcessStartReceiptRow
{
    public required Guid ApplicationId { get; init; }

    /// <summary>The id of the process resource.</summary>
    public required Guid ProcessId { get; init; }

    public required string Key { get; init; }

    /// <summary>The subject record of the start, so the key cannot be reused with another record.</summary>
    public required Guid SubjectId { get; init; }

    public required Guid InstanceId { get; init; }

    public required int StatusCode { get; init; }

    /// <summary>The response body, as JSON text.</summary>
    public required string Body { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
