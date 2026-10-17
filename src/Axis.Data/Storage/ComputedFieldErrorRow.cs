namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.computed_field_errors</c>: a computed field whose expression failed for one record
/// when a release recomputed it. The stored value of the field is <c>NULL</c>. The error text is fixed
/// and never holds a stored value.
/// </summary>
public sealed class ComputedFieldErrorRow
{
    /// <summary>The entity <c>id</c>, or the child entity <c>id</c> for a field of a child row.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>The record id, or the child row id for a field of a child row.</summary>
    public required Guid RecordId { get; init; }

    /// <summary>The computed field name.</summary>
    public required string Field { get; init; }

    /// <summary>The release that computed the row.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>A fixed error text.</summary>
    public required string Error { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
