namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.endpoint_receipts</c>: the record and the instance of an endpoint call that
/// carried an <c>Idempotency-Key</c>. A key is unique per application, endpoint and caller.
/// </summary>
public sealed class EndpointReceiptRow
{
    public required Guid ApplicationId { get; init; }

    /// <summary>The id of the endpoint resource.</summary>
    public required Guid EndpointId { get; init; }

    /// <summary>The caller's id, the <c>sub</c> of its token.</summary>
    public required string Caller { get; init; }

    public required string Key { get; init; }

    public required Guid RecordId { get; init; }

    /// <summary>The instance the call started, or null for an endpoint without a process.</summary>
    public Guid? InstanceId { get; init; }

    /// <summary>The SHA-256 hash of the call's input as canonical JSON, so the key cannot be reused with another input.</summary>
    public required byte[] InputHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
