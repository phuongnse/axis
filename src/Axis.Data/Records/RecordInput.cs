namespace Axis.Data.Records;

/// <summary>A valid record request body.</summary>
public sealed record RecordInput
{
    /// <summary>The fields the body names, in the entity's field declaration order.</summary>
    public required IReadOnlyList<RecordValue> Values { get; init; }

    /// <summary>
    /// The child collections the body names, in the entity's field declaration order. A collection
    /// the body leaves out is not here.
    /// </summary>
    public IReadOnlyList<RecordRows> Rows { get; init; } = [];

    /// <summary>The version the update expects; set on update only.</summary>
    public long? Version { get; init; }
}
