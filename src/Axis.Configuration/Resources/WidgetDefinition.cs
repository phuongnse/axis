namespace Axis.Configuration.Resources;

/// <summary>
/// A widget as written in the page file. It names an entity, a data source or, for a form widget, a
/// form. Whether that binding, its entity, data source, form and form page exist is checked by
/// <see cref="Compilation.ApplicationCompiler"/>, not by the loader.
/// </summary>
public sealed record WidgetDefinition
{
    public required string Type { get; init; }

    /// <summary>The entity whose records the widget shows: shorthand for all records of that entity.</summary>
    public string? Entity { get; init; }

    /// <summary>The data source whose rows a <c>table</c> widget shows, in place of <see cref="Entity"/>.</summary>
    public string? DataSource { get; init; }

    /// <summary>The form whose sections a <c>form</c> widget shows, in place of <see cref="Entity"/>.</summary>
    public string? Form { get; init; }

    /// <summary>
    /// The page that opens a record of a <c>table</c> widget, holding a <c>form</c> widget over the
    /// widget's entity, or over the root entity of its data source. That form widget may name the
    /// entity itself or a form over it.
    /// </summary>
    public string? FormPage { get; init; }
}
