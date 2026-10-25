namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.connector_calls</c>: one connector call that a <c>callConnector</c> step
/// staged. Its id is the call's operation identity. Times use the database clock. No request or
/// response body is stored.
/// </summary>
public sealed class ConnectorCallRow
{
    /// <summary>The call's operation identity, a version 7 UUID.</summary>
    public required Guid Id { get; init; }

    public required Guid ProcessInstanceId { get; init; }

    /// <summary>The declared name of the <c>callConnector</c> step.</summary>
    public required string Step { get; init; }

    /// <summary>
    /// The instance revision the step ran at, which matches the step's history row for that run. A
    /// step stages one call per pass of a loop, and this tells the passes apart. A retry restage sets it to
    /// the revision of the new run.
    /// </summary>
    public required long StagedRevision { get; init; }

    /// <summary>The release the instance is pinned to.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The connector's declared name.</summary>
    public required string Connector { get; init; }

    /// <summary>The operation's declared name.</summary>
    public required string Operation { get; init; }

    /// <summary><c>pending</c>, <c>sending</c>, <c>unknown</c>, <c>succeeded</c> or <c>failed</c>.</summary>
    public required string State { get; init; }

    /// <summary>The attempts made so far, the first one included.</summary>
    public required int Attempts { get; init; }

    /// <summary>
    /// The failed lookups since the call last became <c>unknown</c>. It sets the wait before the
    /// next lookup, and is reset when the call goes back to <c>pending</c>.
    /// </summary>
    public int Lookups { get; init; }

    /// <summary>
    /// The evaluated input as JSON text, sent again by every retry. It is kept after the call
    /// settles. It is never copied to history, audit records or logs.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>The HTTP status of the last attempt or lookup, or <see langword="null"/> when none answered.</summary>
    public int? LastStatus { get; init; }

    /// <summary>The error of the last attempt or lookup. It holds no body or secret.</summary>
    public string? LastError { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the call became <c>succeeded</c> or <c>failed</c>, or <see langword="null"/> before.</summary>
    public DateTimeOffset? SettledAt { get; init; }
}
