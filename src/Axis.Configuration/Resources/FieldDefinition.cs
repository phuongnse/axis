namespace Axis.Configuration.Resources;

/// <summary>
/// A field as written in the entity file. Whether a constraint applies to the field's type is
/// checked by a later compile stage, not by the loader.
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

    public IReadOnlyList<string>? Values { get; init; }
}
