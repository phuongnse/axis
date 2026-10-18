namespace Axis.Configuration.Resources;

/// <summary>
/// A module the application uses, as written in its manifest: the module id and a version range.
/// The range is checked and resolved by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ApplicationModuleDefinition
{
    public required string Id { get; init; }

    /// <summary>The version range as written: <c>1.2.0</c>, <c>^1.2.0</c> or <c>~1.2.0</c>.</summary>
    public required string Version { get; init; }
}
