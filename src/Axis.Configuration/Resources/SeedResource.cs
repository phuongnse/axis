namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>seed</c> resource: records with fixed ids for one entity, inserted once by the startup
/// step. Whether its entity exists is checked by <see cref="Compilation.ApplicationCompiler"/>;
/// its values are checked only when they are inserted.
/// </summary>
public sealed record SeedResource : Resource
{
    public required string Entity { get; init; }

    public required IReadOnlyList<SeedRecordDefinition> Records { get; init; }
}
