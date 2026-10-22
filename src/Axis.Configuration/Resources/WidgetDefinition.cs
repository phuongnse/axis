namespace Axis.Configuration.Resources;

/// <summary>
/// A widget as written in the page file. It names an entity, a data source or, for a form widget, a
/// form. A <c>columns</c>, <c>tabs</c> or <c>section</c> widget is a container: it holds other widgets
/// and binds nothing. Whether that binding, its entity, data source, form and form page exist is checked by
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

    /// <summary>The process a <c>processInspector</c> widget inspects.</summary>
    public string? Process { get; init; }

    /// <summary>The buttons a <c>form</c> widget shows on a saved record, or null when it has none.</summary>
    public IReadOnlyList<WidgetActionDefinition>? Actions { get; init; }

    /// <summary>The <c>navigate</c> buttons a <c>table</c> widget shows on each row, or null when it has none.</summary>
    public IReadOnlyList<WidgetActionDefinition>? RowActions { get; init; }

    /// <summary>The <c>navigate</c> buttons a <c>table</c> widget shows above its rows, or null when it has none.</summary>
    public IReadOnlyList<WidgetActionDefinition>? ToolbarActions { get; init; }

    /// <summary>The columns of a <c>columns</c> widget, or null on any other widget.</summary>
    public IReadOnlyList<WidgetColumnDefinition>? Columns { get; init; }

    /// <summary>The tabs of a <c>tabs</c> widget, or null on any other widget.</summary>
    public IReadOnlyList<WidgetTabDefinition>? Tabs { get; init; }

    /// <summary>The title of a <c>section</c> widget, or null on any other widget.</summary>
    public TextReference? Title { get; init; }

    /// <summary>Whether a <c>section</c> widget starts collapsed. Null when the file does not say, which means no.</summary>
    public bool? Collapsed { get; init; }

    /// <summary>The widgets of a <c>section</c> widget, or null on any other widget.</summary>
    public IReadOnlyList<WidgetDefinition>? Widgets { get; init; }
}

/// <summary>One column of a <c>columns</c> widget: a width out of 12 and the widgets it holds.</summary>
public sealed record WidgetColumnDefinition
{
    public required int Width { get; init; }

    public required IReadOnlyList<WidgetDefinition> Widgets { get; init; }
}

/// <summary>One tab of a <c>tabs</c> widget: a label and the widgets it holds.</summary>
public sealed record WidgetTabDefinition
{
    public required TextReference Label { get; init; }

    public required IReadOnlyList<WidgetDefinition> Widgets { get; init; }
}
