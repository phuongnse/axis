namespace Axis.Configuration.Resources;

/// <summary>
/// A widget as written in the page file. It names an entity, a data source or, for a form widget, a
/// form. A <c>kpi</c> widget names a data source and one of its measures. A <c>chart</c> widget names a data source, its category and its measures. A <c>detail</c> widget names an entity
/// or a form, like a form widget, and the page parameter that picks its record. A <c>columns</c>, <c>tabs</c> or <c>section</c> widget is a container: it holds other widgets
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

    /// <summary>The <c>startProcess</c> and <c>navigate</c> buttons a <c>table</c> widget shows on each row, or null when it has none.</summary>
    public IReadOnlyList<WidgetActionDefinition>? RowActions { get; init; }

    /// <summary>The <c>navigate</c> buttons a <c>table</c> widget shows above its rows, or null when it has none.</summary>
    public IReadOnlyList<WidgetActionDefinition>? ToolbarActions { get; init; }

    /// <summary>
    /// The page parameter that gives each data source parameter its value on a <c>table</c> widget over a data source,
    /// keyed by the data source parameter name, or null when it binds none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>The columns of a <c>columns</c> widget, or null on any other widget.</summary>
    public IReadOnlyList<WidgetColumnDefinition>? Columns { get; init; }

    /// <summary>The tabs of a <c>tabs</c> widget, or null on any other widget.</summary>
    public IReadOnlyList<WidgetTabDefinition>? Tabs { get; init; }

    /// <summary>The title of a <c>section</c> or <c>chart</c> widget, or null on any other widget.</summary>
    public TextReference? Title { get; init; }

    /// <summary>Whether a <c>section</c> widget starts collapsed. Null when the file does not say, which means no.</summary>
    public bool? Collapsed { get; init; }

    /// <summary>The widgets of a <c>section</c> widget, or null on any other widget.</summary>
    public IReadOnlyList<WidgetDefinition>? Widgets { get; init; }

    /// <summary>The page parameter that picks the record a <c>detail</c> widget shows, or null on any other widget.</summary>
    public string? Record { get; init; }

    /// <summary>The measure of its data source that a <c>kpi</c> widget shows, or null on any other widget.</summary>
    public string? Measure { get; init; }

    /// <summary>The label of a <c>kpi</c> widget, or null on any other widget.</summary>
    public TextReference? Label { get; init; }

    /// <summary>The second measure a <c>kpi</c> widget shows next to the first, or null when it has none.</summary>
    public KpiComparisonDefinition? Comparison { get; init; }

    /// <summary>The <c>navigate</c> action a <c>kpi</c> widget opens when clicked, or null when it has none.</summary>
    public WidgetActionDefinition? Navigate { get; init; }

    /// <summary>The kind of a <c>chart</c> widget: <c>bar</c>, <c>line</c> or <c>pie</c>. Null on any other widget.</summary>
    public string? Kind { get; init; }

    /// <summary>The group field of its data source that a <c>chart</c> widget draws on its category axis, or null on any other widget.</summary>
    public string? Category { get; init; }

    /// <summary>The measures of its data source that a <c>chart</c> widget draws, or null on any other widget.</summary>
    public IReadOnlyList<ChartMeasureDefinition>? Measures { get; init; }
}

/// <summary>One measure of a <c>chart</c> widget: a measure of its data source and the label of its series.</summary>
public sealed record ChartMeasureDefinition
{
    public required string Name { get; init; }

    public required TextReference Label { get; init; }
}

/// <summary>The comparison of a <c>kpi</c> widget: a second measure of the same data source and its label.</summary>
public sealed record KpiComparisonDefinition
{
    public required string Measure { get; init; }

    public required TextReference Label { get; init; }
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
