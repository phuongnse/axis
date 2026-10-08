namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>seed</c> resource: records with fixed ids for one entity, inserted once by the startup
/// step, or kept in step with the file when <see cref="Sync"/> is set. Whether its entity exists
/// is checked by <see cref="Compilation.ApplicationCompiler"/>; its values are checked only when
/// they are written.
/// </summary>
public sealed record SeedResource : Resource
{
    public required string Entity { get; init; }

    /// <summary>Whether the startup step also updates existing records whose declared values differ.</summary>
    public bool Sync { get; init; }

    public required IReadOnlyList<SeedRecordDefinition> Records { get; init; }
}
