using Axis.Configuration.Model;

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
    /// failed; the errors say which. <see cref="RecordCommands.SetAsync"/> returns it for any of these.
    /// <see cref="RecordCommands.CreateAsync"/> and <see cref="RecordCommands.UpdateAsync"/> return it for a
    /// core validation of the entity's module contract that failed, with its point.
    /// </summary>
    Invalid,

    /// <summary>
    /// The entity's lifecycle refuses the write: a create outside an initial state, a state change along
    /// no transition or along one whose guard is not true. The errors say which.
    /// </summary>
    LifecycleRefused,

    /// <summary>
    /// An effect of the transition the update made could not be written, so the whole write is refused.
    /// <see cref="RecordWriteResult.Point"/> names the effect, such as <c>Claim/effects/entryRecorded</c>.
    /// </summary>
    EffectRefused,
}

/// <summary>A record that an effect of a transition created. <see cref="Point"/> names the effect, such as <c>Claim/effects/entryRecorded</c>.</summary>
public sealed record EffectRecord(string Point, EntityModel Entity, Record Record);

/// <summary>
/// The outcome of a record create or update. <see cref="Errors"/> is set for
/// <see cref="RecordWriteOutcome.MissingReference"/>, <see cref="RecordWriteOutcome.UniqueViolation"/>
/// <see cref="RecordWriteOutcome.Invalid"/> and <see cref="RecordWriteOutcome.LifecycleRefused"/>,
/// keyed by RFC 6901 JSON Pointer such as <c>/values/category</c>, with fixed messages that
/// never name a table, column or constraint.
/// <see cref="Point"/> is the named point of the module contract that refused the write, such as
/// <c>Request/transitions/submit</c> or <c>Request/validations/amountFrozen</c>, or <see langword="null"/> when none is named.
/// <see cref="RecordWriteOutcome.EffectRefused"/> names its effect in <see cref="Point"/> and has no errors.
/// <see cref="Effects"/> holds the records the effects of a written update created.
/// </summary>
public sealed record RecordWriteResult(
    RecordWriteOutcome Outcome,
    Record? Record = null,
    SortedDictionary<string, string[]>? Errors = null,
    string? Point = null)
{
    /// <summary>
    /// The records that the effects of the transition created, in declaration order. It is set on a
    /// <see cref="RecordWriteOutcome.Written"/> update that made a transition with effects, and empty otherwise.
    /// </summary>
    public IReadOnlyList<EffectRecord> Effects { get; init; } = [];
}
