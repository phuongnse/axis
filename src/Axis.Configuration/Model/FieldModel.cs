using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled field. Type-specific properties are set only for the type they belong to:
/// <see cref="MaxLength"/> and <see cref="MinLength"/> for text, <see cref="Min"/> and
/// <see cref="Max"/> for integer, decimal, date and date-time, <see cref="Precision"/> and <see cref="Scale"/> for decimal and
/// money, <see cref="Currency"/> and <see cref="CurrencyField"/> for money, <see cref="Values"/> for enum,
/// <see cref="Target"/> for reference, references and child collection,
/// <see cref="TargetDisplayField"/> for reference and references, <see cref="MaxSize"/> and <see cref="Accept"/> for file,
/// and <see cref="Sequence"/>, <see cref="Creator"/> and <see cref="Searchable"/> for text, and <see cref="Default"/> for text,
/// integer, decimal, money, boolean, date, date-time and enum.
/// </summary>
public sealed record FieldModel
{
    public required string Name { get; init; }

    public required FieldType Type { get; init; }

    public TextReference? Label { get; init; }

    public bool Required { get; init; }

    public bool Unique { get; init; }

    public int? MaxLength { get; init; }

    public int? MinLength { get; init; }

    /// <summary>
    /// The lowest value the record API accepts, as the literal was written: the integer or decimal
    /// number text, or the date or date-time string. Null when the field has no lower limit.
    /// </summary>
    public string? Min { get; init; }

    /// <summary>The highest value the record API accepts, written as <see cref="Min"/> is. Null when the field has no upper limit.</summary>
    public string? Max { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    /// <summary>The largest size in bytes of a file field's file, or null when the host limit applies.</summary>
    public long? MaxSize { get; init; }

    /// <summary>The media types a file field accepts, or null for any.</summary>
    public IReadOnlyList<string>? Accept { get; init; }

    /// <summary>The ISO 4217 code of the fixed currency, set only for money fields that declare one.</summary>
    public string? Currency { get; init; }

    /// <summary>The declared name of the field that holds the currency, set only for money fields that declare one.</summary>
    public string? CurrencyField { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>The text key of each enum value, or null when the field declares no labels.</summary>
    public IReadOnlyDictionary<string, TextReference>? ValueLabels { get; init; }

    public EntityReference? Target { get; init; }

    /// <summary>The declared name of the target entity's display field, set only for reference and references fields.</summary>
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

    /// <summary>Whether a search matches the field. Activation gives the field a trigram index.</summary>
    public bool Searchable { get; init; }

    /// <summary>The checked default, applied on create when the body leaves the field out, or null.</summary>
    public ExpressionModel? Default { get; init; }

    /// <summary>
    /// Whether the field's value never appears in process inspection. The record API ignores it, and
    /// it changes no table.
    /// </summary>
    public bool Sensitive { get; init; }

    /// <summary>
    /// Whether the field is stored in a column of its entity's table. A child collection has no
    /// column: its rows live in the child entity's table. A references field has none either: its
    /// links live in a table of their own.
    /// </summary>
    public bool HasColumn => Type is not (FieldType.ChildCollection or FieldType.References);
}
