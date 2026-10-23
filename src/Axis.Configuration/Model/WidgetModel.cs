using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled widget. On a table or form widget, exactly one of <see cref="Entity"/> and
/// <see cref="DataSource"/> is set: the widget shows all records of an entity, or the rows of a data
/// source. A task inbox lists the signed-in user's tasks, so every reference on it is null. <see cref="Form"/> is set
/// only on a form widget that names a form, and <see cref="Entity"/> is then the form's entity.
/// <see cref="Actions"/> is empty on every widget but a form widget that declares
/// actions, and <see cref="RowActions"/> and <see cref="ToolbarActions"/> are empty on every widget but a table
/// that declares them. <see cref="Parameters"/> is empty on every widget but a table over a data source that binds
/// page parameters. <see cref="Process"/> is set only on a process inspector, which has no other reference.
/// A <c>columns</c>, <c>tabs</c> or <c>section</c> widget is a container with no reference of its own: it holds
/// <see cref="Columns"/>, <see cref="Tabs"/> or <see cref="Widgets"/>, and every other widget leaves those empty.
/// A <c>kpi</c> widget sets <see cref="DataSource"/> and <see cref="Kpi"/>, and no other reference. A <c>chart</c> widget
/// sets <see cref="DataSource"/> and <see cref="Chart"/>, and no other reference. A <c>detail</c>
/// widget sets <see cref="Entity"/>, or <see cref="Form"/> and its entity, and <see cref="Detail"/>, and can hold
/// <see cref="Actions"/>, as a form widget does.
/// Records compare lists by reference, so two widgets with containers are equal only when they share those lists.
/// </summary>
public sealed record WidgetModel(
    WidgetType Type,
    EntityReference? Entity,
    DataSourceReference? DataSource = null,
    FormReference? Form = null,
    ProcessReference? Process = null)
{
    /// <summary>The <c>startProcess</c> and <c>navigate</c> actions of a form widget, in page file order.</summary>
    public IReadOnlyList<WidgetActionModel> Actions { get; init; } = [];

    /// <summary>The <c>startProcess</c> and <c>navigate</c> actions a table widget shows on each row, in page file order.</summary>
    public IReadOnlyList<WidgetActionModel> RowActions { get; init; } = [];

    /// <summary>The <c>navigate</c> actions a table widget shows above its rows, in page file order.</summary>
    public IReadOnlyList<WidgetActionModel> ToolbarActions { get; init; } = [];

    /// <summary>
    /// The page parameter that gives each data source parameter its value, keyed by the declared name of the data
    /// source parameter, with the declared name of the page parameter as the value.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = NavigateActionModel.NoParameters;

    /// <summary>The columns of a <c>columns</c> widget, in page file order.</summary>
    public IReadOnlyList<WidgetColumnModel> Columns { get; init; } = [];

    /// <summary>The tabs of a <c>tabs</c> widget, in page file order.</summary>
    public IReadOnlyList<WidgetTabModel> Tabs { get; init; } = [];

    /// <summary>The title of a <c>section</c> widget, or null on any other widget. A chart keeps its title in <see cref="Chart"/>.</summary>
    public TextReference? Title { get; init; }

    /// <summary>Whether a <c>section</c> widget starts collapsed.</summary>
    public bool Collapsed { get; init; }

    /// <summary>The widgets of a <c>section</c> widget, in page file order.</summary>
    public IReadOnlyList<WidgetModel> Widgets { get; init; } = [];

    /// <summary>What a <c>kpi</c> widget shows of its data source, or null on any other widget.</summary>
    public KpiModel? Kpi { get; init; }

    /// <summary>What a <c>chart</c> widget draws of its data source, or null on any other widget.</summary>
    public ChartModel? Chart { get; init; }

    /// <summary>What a <c>detail</c> widget shows, or null on any other widget.</summary>
    public DetailModel? Detail { get; init; }
}

/// <summary>
/// What a <c>detail</c> widget shows: the record that the page parameter <paramref name="Record"/> picks, as the
/// page declares the name of the parameter.
/// </summary>
public sealed record DetailModel(string Record);

/// <summary>
/// What a <c>kpi</c> widget shows: one measure of the single total row of its data source, as the data
/// source declares the measure's name, with a label, an optional <paramref name="Comparison"/> and an
/// optional <paramref name="Navigate"/> action that a click opens.
/// </summary>
public sealed record KpiModel(string Measure, TextReference Label, KpiComparisonModel? Comparison, NavigateActionModel? Navigate);

/// <summary>The second measure of a <c>kpi</c> widget, as its data source declares the name, and its label.</summary>
public sealed record KpiComparisonModel(string Measure, TextReference Label);

/// <summary>The kind of a <c>chart</c> widget.</summary>
public enum ChartKind
{
    Bar,
    Line,
    Pie,
}

/// <summary>
/// What a <c>chart</c> widget draws: the groups of its grouped data source, with the group field <paramref name="Category"/>
/// and the <paramref name="Measures"/> as the data source declares their names. The title is the chart's accessible name.
/// Records compare lists by reference, so two charts are equal only when they share the list.
/// </summary>
public sealed record ChartModel(ChartKind Kind, TextReference Title, string Category, IReadOnlyList<ChartMeasureModel> Measures);

/// <summary>One series of a <c>chart</c> widget: a measure, as its data source declares the name, and its label.</summary>
public sealed record ChartMeasureModel(string Name, TextReference Label);

/// <summary>One column of a <c>columns</c> widget. The widths of a widget's columns add up to 12.</summary>
public sealed record WidgetColumnModel(int Width, IReadOnlyList<WidgetModel> Widgets);

/// <summary>One tab of a <c>tabs</c> widget.</summary>
public sealed record WidgetTabModel(TextReference Label, IReadOnlyList<WidgetModel> Widgets);
