namespace Axis.Configuration.Resources;

/// <summary>
/// An extension package the application uses, as written in its manifest: the package id and a
/// version range. The range is checked and resolved by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ApplicationPackageDefinition
{
    public required string Id { get; init; }

    /// <summary>The version range as written: <c>1.2.0</c>, <c>^1.2.0</c> or <c>~1.2.0</c>.</summary>
    public required string Version { get; init; }
}
