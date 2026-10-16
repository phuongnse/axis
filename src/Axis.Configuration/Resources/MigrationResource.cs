namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>migration</c> resource: an ordered list of changes to the storage of one entity. The schema
/// checks its shape and which properties each change type takes. Its entity, the fields its
/// changes name and the values of its <c>fill</c> and <c>convert</c> changes are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record MigrationResource : Resource
{
    /// <summary>The name of the entity the migration changes.</summary>
    public required string Entity { get; init; }

    /// <summary>The changes, in the order they run. The schema requires at least one.</summary>
    public required IReadOnlyList<MigrationChangeDefinition> Changes { get; init; }
}
