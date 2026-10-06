using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled field. Type-specific properties are set only for the type they belong to:
/// <see cref="MaxLength"/> for text, <see cref="Precision"/> and <see cref="Scale"/> for decimal,
/// <see cref="Values"/> for enum, and <see cref="Target"/> and <see cref="TargetDisplayField"/> for reference.
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

    public IReadOnlyList<string>? Values { get; init; }

    public EntityReference? Target { get; init; }

    /// <summary>The declared name of the target entity's display field, set only for reference fields.</summary>
    public string? TargetDisplayField { get; init; }
}
