namespace Axis.Data.Records;

/// <summary>
/// The outcome of a record delete. <see cref="Version"/> is the version the record had when it was
/// deleted, set only for <see cref="RecordDeleteOutcome.Deleted"/>. <see cref="Errors"/> and
/// <see cref="Point"/> are set only for <see cref="RecordDeleteOutcome.LifecycleRefused"/>: the
/// errors are keyed <c>/values/&lt;state field&gt;</c>, and the point is the named point of the
/// module contract that refused the delete.
/// </summary>
public sealed record RecordDeleteResult(
    RecordDeleteOutcome Outcome,
    long? Version = null,
    SortedDictionary<string, string[]>? Errors = null,
    string? Point = null);
