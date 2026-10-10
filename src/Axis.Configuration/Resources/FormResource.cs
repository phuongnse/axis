namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>form</c> resource: the layout of one entity's fields in titled sections, where any field
/// can be read-only. Whether its entity, fields and title keys exist, and whether a field is
/// listed twice, is checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record FormResource : Resource
{
    public required string Entity { get; init; }

    /// <summary>The sections in display order.</summary>
    public required IReadOnlyList<FormSectionDefinition> Sections { get; init; }
}
