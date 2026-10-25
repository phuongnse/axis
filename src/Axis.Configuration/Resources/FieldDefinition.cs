namespace Axis.Configuration.Resources;

/// <summary>
/// A field as written in the entity file. Whether a constraint applies to the field's type, and
/// whether its value is valid for storage, is checked by
/// <see cref="Compilation.ApplicationCompiler"/>, not by the loader.
/// </summary>
public sealed record FieldDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public TextReference? Label { get; init; }

    public bool? Required { get; init; }

    public bool? Unique { get; init; }

    public int? MaxLength { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public string? Target { get; init; }

    /// <summary>The ISO 4217 code of a money field's fixed currency, or null when not declared.</summary>
    public string? Currency { get; init; }

    /// <summary>The name of the enum or text field of the same entity that holds a money field's currency, or null when not declared.</summary>
    public string? CurrencyField { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    public IReadOnlyDictionary<string, TextReference>? ValueLabels { get; init; }

    /// <summary>The expression of a computed field, or null for a field that clients write.</summary>
    public string? Expression { get; init; }

    /// <summary>The name of the sequence that numbers the field, or null when it has none.</summary>
    public string? Sequence { get; init; }

    /// <summary>Whether the server sets the field to the id of the user who creates the record, or null when not declared.</summary>
    public bool? Creator { get; init; }

    /// <summary>The expression that gives the field its value on create when the body leaves it out, or null when it has none.</summary>
    public string? Default { get; init; }

    /// <summary>Whether the field's value is kept out of process inspection, or null when not declared.</summary>
    public bool? Sensitive { get; init; }
}
