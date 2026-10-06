namespace Axis.Configuration.Resources;

/// <summary>
/// A widget as written in the page file. Whether its entity and form page exist is checked by
/// <see cref="Compilation.ApplicationCompiler"/>, not by the loader.
/// </summary>
public sealed record WidgetDefinition
{
    public required string Type { get; init; }

    public required string Entity { get; init; }

    /// <summary>The page that opens a record of a <c>table</c> widget, holding a <c>form</c> widget over the same entity.</summary>
    public string? FormPage { get; init; }
}
