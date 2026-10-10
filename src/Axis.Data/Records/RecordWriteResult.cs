namespace Axis.Data.Records;

/// <summary>How a record create or update ended.</summary>
public enum RecordWriteOutcome
{
    /// <summary>The record was written; <see cref="RecordWriteResult.Record"/> holds it.</summary>
    Written,

    /// <summary>No record has the id.</summary>
    NotFound,

    /// <summary>The record exists, but its version is not the one the update expects.</summary>
    StaleVersion,

    /// <summary>A reference value names no record of the target entity; the errors say which.</summary>
    MissingReference,

    /// <summary>A value repeats the value of a unique field; the errors say which.</summary>
    UniqueViolation,

    /// <summary>The table has a constraint the active model does not declare, such as an undeclared <c>NOT NULL</c>.</summary>
    SchemaConflict,

    /// <summary>
    /// A value does not fit its field, a computed field could not be computed, or a validation
    /// failed; the errors say which. Only <see cref="RecordCommands.SetAsync"/> returns it.
    /// </summary>
    Invalid,
}

/// <summary>
/// The outcome of a record create or update. <see cref="Errors"/> is set for
/// <see cref="RecordWriteOutcome.MissingReference"/>, <see cref="RecordWriteOutcome.UniqueViolation"/>
/// and <see cref="RecordWriteOutcome.Invalid"/>,
/// keyed by RFC 6901 JSON Pointer such as <c>/values/category</c>, with fixed messages that
/// never name a table, column or constraint.
/// </summary>
public sealed record RecordWriteResult(RecordWriteOutcome Outcome, Record? Record = null, SortedDictionary<string, string[]>? Errors = null);
