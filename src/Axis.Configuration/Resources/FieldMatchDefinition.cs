namespace Axis.Configuration.Resources;

/// <summary>
/// The <c>match</c> of a reference field as written in the entity file. Whether <see cref="Field"/>
/// is a unique field of the target and <see cref="From"/> a field of the same entity with the same
/// type is checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record FieldMatchDefinition
{
    /// <summary>The name of the unique field of the target entity that the record is found by.</summary>
    public required string Field { get; init; }

    /// <summary>The name of the field of this entity whose value is looked up in <see cref="Field"/>.</summary>
    public required string From { get; init; }
}
