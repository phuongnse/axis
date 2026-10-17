namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.pending_computed_fields</c>: an entity whose stored computed values a release left
/// for the worker to recompute in batches, because more rows were affected than the inline recompute
/// limit allows. The row goes when a batch finds no out-of-date row.
/// </summary>
public sealed class PendingComputedFieldRow
{
    /// <summary>The entity <c>id</c>. An entity is pending at most once.</summary>
    public required Guid EntityId { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The release whose computed definitions the backfill stores and stamps.</summary>
    public required Guid TargetReleaseId { get; init; }

    /// <summary>
    /// The releases whose stored fingerprint for the entity equals the target's, and the target itself.
    /// A row stamped by one of them is up to date.
    /// </summary>
    public required Guid[] CurrentReleaseIds { get; init; }

    /// <summary>The computed field names of the entity and of its child entities.</summary>
    public required string[] Fields { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
