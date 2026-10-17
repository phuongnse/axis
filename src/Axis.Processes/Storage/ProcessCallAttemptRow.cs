namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.process_call_attempts</c>: one attempt, or one reconciliation lookup, of a
/// connector call or a package call. Times use the database clock. No request or response body, no
/// exception message and no secret is stored.
/// </summary>
public sealed class ProcessCallAttemptRow
{
    /// <summary>A version 7 UUID.</summary>
    public required Guid Id { get; init; }

    /// <summary><c>connector</c> or <c>package</c>.</summary>
    public required string CallKind { get; init; }

    /// <summary>The call, in <c>axis.connector_calls</c> or <c>axis.package_calls</c> as <see cref="CallKind"/> says.</summary>
    public required Guid CallId { get; init; }

    /// <summary><c>attempt</c> or <c>lookup</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The number of the attempt or lookup, counted per <see cref="Kind"/> from 1.</summary>
    public required int Number { get; init; }

    /// <summary>When the attempt or lookup started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When its outcome was recorded.</summary>
    public required DateTimeOffset FinishedAt { get; init; }

    /// <summary>
    /// For an attempt <c>succeeded</c>, <c>retryable</c>, <c>unknown</c> or <c>failed</c>. For a lookup
    /// <c>found</c>, <c>notFound</c> or <c>failed</c>.
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>The HTTP status, or <see langword="null"/> for a package call and when none answered.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>A fixed text, such as a timeout. It holds no body, no exception message and no secret.</summary>
    public string? Error { get; init; }

    /// <summary>The trace id of the run that made the attempt or lookup, or <see langword="null"/> when no span was current.</summary>
    public string? TraceId { get; init; }
}
