namespace Axis.Data.Records;

/// <summary>
/// The outcome of a record delete. <see cref="Version"/> is the version the record had when it was
/// deleted, set only for <see cref="RecordDeleteOutcome.Deleted"/>.
/// </summary>
public sealed record RecordDeleteResult(RecordDeleteOutcome Outcome, long? Version = null);
