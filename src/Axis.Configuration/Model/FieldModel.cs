using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled field. Type-specific properties are set only for the type they belong to:
/// <see cref="MaxLength"/> for text, <see cref="Precision"/> and <see cref="Scale"/> for decimal,
/// <see cref="Values"/> for enum, <see cref="Target"/> for reference and child collection,
/// <see cref="TargetDisplayField"/> for reference, <see cref="MaxSize"/> and <see cref="Accept"/>
/// for file, <see cref="Sequence"/> and <see cref="Creator"/> for text, and <see cref="Default"/>
/// for text, integer, decimal, boolean, date, date-time and enum.
/// </summary>
public sealed record FieldModel
{
    public required string Name { get; init; }

    public required FieldType Type { get; init; }

    public TextReference? Label { get; init; }

    public bool Required { get; init; }

    public bool Unique { get; init; }

    public int? MaxLength { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    /// <summary>The largest size in bytes of a file field's file, or null when the host limit applies.</summary>
    public long? MaxSize { get; init; }

    /// <summary>The media types a file field accepts, or null for any.</summary>
    public IReadOnlyList<string>? Accept { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>The text key of each enum value, or null when the field declares no labels.</summary>
    public IReadOnlyDictionary<string, TextReference>? ValueLabels { get; init; }

    public EntityReference? Target { get; init; }

    /// <summary>The declared name of the target entity's display field, set only for reference fields.</summary>
    public string? TargetDisplayField { get; init; }

    /// <summary>The expression of a computed field, or null for a field that clients write.</summary>
    public ComputedFieldModel? Computed { get; init; }

    /// <summary>Whether the server computes the field's value on every write. Clients cannot set it.</summary>
    public bool IsComputed => Computed is not null;

    /// <summary>The sequence that numbers the field, or null when it has none.</summary>
    public SequenceModel? Sequence { get; init; }

    /// <summary>
    /// Whether the server sets the field to the id of the signed-in user who creates the record.
    /// Clients cannot set it.
    /// </summary>
    public bool Creator { get; init; }

    /// <summary>The checked default, applied on create when the body leaves the field out, or null.</summary>
    public ExpressionModel? Default { get; init; }

    /// <summary>
    /// Whether the field's value never appears in process inspection. The record API ignores it, and
    /// it changes no table.
    /// </summary>
    public bool Sensitive { get; init; }

    /// <summary>
    /// Whether the field is stored in a column of its entity's table. A child collection has no
    /// column: its rows live in the child entity's table.
    /// </summary>
    public bool HasColumn => Type != FieldType.ChildCollection;
}
