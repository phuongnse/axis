using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.Presentation.Sites;

/// <summary>The sites of every active application in the tenant, in path order.</summary>
public sealed record SiteList(IReadOnlyList<SiteListItem> Sites);

/// <summary>One site in the list.</summary>
/// <param name="Titles">The site title in each available locale, keyed by the locale as the site declares it.</param>
public sealed record SiteListItem(string Path, string TitleKey, IReadOnlyDictionary<string, string> Titles);

/// <summary>What the SPA needs to build an application site's shell: title, locales and navigation.</summary>
/// <param name="TitleKey">Text key of the site title.</param>
public sealed record ApplicationSite(string Path, string TitleKey, SiteLocales Locales, IReadOnlyList<SiteNavigationItem> Navigation);

/// <summary>One navigation entry of an application site: the page it opens and the text key of its label.</summary>
public sealed record SiteNavigationItem(string Page, string LabelKey);

/// <summary>
/// What the SPA needs to render a page: its title, its parameters and its widgets. The widgets are the top level of a tree: a
/// <see cref="ColumnsWidgetMetadata"/>, <see cref="TabsWidgetMetadata"/> or <see cref="SectionWidgetMetadata"/> holds
/// the others.
/// </summary>
/// <param name="Parameters">The typed values the page reads from its URL, in file order. Empty when the page declares none.</param>
public sealed record PageMetadata(string Name, string TitleKey, IReadOnlyList<PageParameterMetadata> Parameters, IReadOnlyList<PageWidgetMetadata> Widgets);

/// <summary>
/// One parameter of a page. <see cref="Values"/> is set for enum and <see cref="Target"/> for reference.
/// </summary>
/// <param name="Type">The type name as written in page files, such as <c>date-time</c>.</param>
public sealed record PageParameterMetadata(
    string Name,
    string Type,
    bool Required,
    IReadOnlyList<string>? Values,
    ReferenceTarget? Target);

/// <summary>
/// One widget of a page: a <see cref="WidgetMetadata"/> that shows something, or a container around other widgets.
/// Its JSON is that of the runtime type, and every kind writes <c>type</c> as its widget type name.
/// </summary>
[JsonConverter(typeof(PageWidgetMetadataConverter))]
public abstract record PageWidgetMetadata
{
    /// <summary>The widget type name as written in page files, such as <c>table</c> or <c>columns</c>.</summary>
    public abstract string Type { get; init; }

    /// <summary>
    /// The JSON Pointer of the widget in the draft page, such as <c>/widgets/0/columns/1/widgets/0</c>. Only the draft page
    /// metadata route of the authoring API sets it. The site's page metadata never writes it.
    /// </summary>
    [JsonPropertyOrder(-1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}

/// <summary>Writes a page widget by its runtime type, so each kind has only its own properties.</summary>
public sealed class PageWidgetMetadataConverter : JsonConverter<PageWidgetMetadata>
{
    public override PageWidgetMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Page metadata is only written.");

    public override void Write(Utf8JsonWriter writer, PageWidgetMetadata value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

/// <summary>
/// A widget of a draft page that does not compile, which the page designer draws as a placeholder. It writes
/// <c>{ type, path, placeholder: true }</c>.
/// </summary>
public sealed record PlaceholderWidgetMetadata : PageWidgetMetadata
{
    /// <summary>The kind of every placeholder, which is not the widget type the draft wrote. <see cref="DraftType"/> is that.</summary>
    [JsonIgnore]
    public override string Type { get; init; } = "placeholder";

    /// <summary>The <c>type</c> the draft wrote, or null when the draft has none or it is not a string.</summary>
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-1)]
    public string? DraftType { get; init; }

    public bool Placeholder { get; } = true;
}

/// <summary>A <c>columns</c> widget: its columns in page file order.</summary>
public sealed record ColumnsWidgetMetadata(IReadOnlyList<ColumnMetadata> Columns) : PageWidgetMetadata
{
    [JsonPropertyOrder(-1)]
    public override string Type { get; init; } = "columns";
}

/// <summary>One column of a <c>columns</c> widget: its width out of 12 and the widgets it holds.</summary>
public sealed record ColumnMetadata(int Width, IReadOnlyList<PageWidgetMetadata> Widgets);

/// <summary>A <c>tabs</c> widget: its tabs in page file order.</summary>
public sealed record TabsWidgetMetadata(IReadOnlyList<TabMetadata> Tabs) : PageWidgetMetadata
{
    [JsonPropertyOrder(-1)]
    public override string Type { get; init; } = "tabs";
}

/// <summary>One tab of a <c>tabs</c> widget: the text key of its label and the widgets it holds.</summary>
public sealed record TabMetadata(string LabelKey, IReadOnlyList<PageWidgetMetadata> Widgets);

/// <summary>A <c>section</c> widget: a titled group of widgets that may start collapsed.</summary>
public sealed record SectionWidgetMetadata(string TitleKey, bool Collapsed, IReadOnlyList<PageWidgetMetadata> Widgets) : PageWidgetMetadata
{
    [JsonPropertyOrder(-1)]
    public override string Type { get; init; } = "section";
}

/// <summary>
/// One widget. On a table or form widget, exactly one of <see cref="Entity"/> and
/// <see cref="DataSource"/> is set: the widget shows all records of an entity, or the rows of a data
/// source. <see cref="Form"/> is set only on a form widget that names a form; <see cref="Entity"/>
/// is then the form's entity, with every field the user may read, so a client knows the type of each
/// field the form shows. <see cref="TasksPath"/> is set only on a task inbox, which has no entity or data source.
/// <see cref="Process"/> and <see cref="InstancesPath"/> are set only on a process inspector, which has no entity or data source either.
/// <see cref="Actions"/> is empty on every widget but a form or detail widget that declares actions.
/// <see cref="RowActions"/> and <see cref="ToolbarActions"/> are empty on every widget but a table that declares them.
/// <see cref="Parameters"/> is empty on every widget but a table over a data source that binds page parameters.
/// <see cref="Access"/> is set on a table, form or detail widget and null on a task inbox, a process inspector, a kpi and a chart.
/// <see cref="Detail"/> is set only on a detail widget, which has an <see cref="Entity"/> and optionally a <see cref="Form"/>.
/// <see cref="Kpi"/> is set only on a kpi widget, which has a <see cref="DataSource"/> and no entity.
/// <see cref="Chart"/> is set only on a chart widget, which has a <see cref="DataSource"/> and no entity.
/// </summary>
/// <param name="TasksPath">The path of the application's task API, which lists the signed-in user's open tasks.</param>
/// <param name="Process">The process an inspector inspects, by its declared name.</param>
/// <param name="InstancesPath">The path of the process's instance list endpoint, so a client never builds it.</param>
/// <param name="Actions">The <c>startProcess</c> and <c>navigate</c> actions of a form widget, in page file order.</param>
/// <param name="RowActions">The <c>startProcess</c> and <c>navigate</c> actions of a table widget on each row, in page file order.</param>
/// <param name="ToolbarActions">The <c>navigate</c> actions of a table widget above its rows, in page file order.</param>
/// <param name="Parameters">The name of the page parameter that gives each data source parameter its value, by the data source parameter's name.</param>
/// <param name="Access">
/// What the user may do with the widget's entity, or with the root entity of its data source.
/// </param>
/// <param name="Detail">What a detail widget shows, or null on any other widget.</param>
/// <param name="Kpi">What a kpi widget shows, or null on any other widget.</param>
/// <param name="Chart">What a chart widget draws, or null on any other widget.</param>
public sealed record WidgetMetadata(
    string Type,
    EntityMetadata? Entity,
    DataSourceMetadata? DataSource,
    FormMetadata? Form,
    string? TasksPath,
    string? Process,
    string? InstancesPath,
    IReadOnlyList<ActionMetadata> Actions,
    IReadOnlyList<ActionMetadata> RowActions,
    IReadOnlyList<ActionMetadata> ToolbarActions,
    IReadOnlyDictionary<string, string> Parameters,
    WidgetAccess? Access,
    DetailMetadata? Detail,
    KpiMetadata? Kpi,
    ChartMetadata? Chart) : PageWidgetMetadata;

/// <summary>What a detail widget shows: the record that one page parameter picks.</summary>
/// <param name="Record">The page parameter whose value is the id of the record, by the name the page declares.</param>
public sealed record DetailMetadata(string Record);

/// <summary>What a kpi widget shows of its data source's one total row.</summary>
/// <param name="Measure">The measure to show, by the name the data source declares.</param>
/// <param name="LabelKey">The text key of the tile's label.</param>
/// <param name="Comparison">A second measure shown next to the first, or null when the widget has none.</param>
/// <param name="Navigate">The action a click opens, or null when the widget has none or the user may not read its page.</param>
public sealed record KpiMetadata(string Measure, string LabelKey, KpiComparisonMetadata? Comparison, NavigateActionMetadata? Navigate);

/// <summary>What a chart widget draws of the groups of its data source.</summary>
/// <param name="Kind">The kind of chart: <c>bar</c>, <c>line</c> or <c>pie</c>.</param>
/// <param name="TitleKey">The text key of the chart's title, which is also its accessible name.</param>
/// <param name="Category">The group field of the data source that names each group, by the name the data source declares.</param>
/// <param name="Measures">The measures to draw, one series each, in page file order.</param>
public sealed record ChartMetadata(string Kind, string TitleKey, string Category, IReadOnlyList<ChartMeasureMetadata> Measures);

/// <summary>One series of a chart widget.</summary>
/// <param name="Name">The measure, by the name the data source declares.</param>
/// <param name="LabelKey">The text key of its label.</param>
public sealed record ChartMeasureMetadata(string Name, string LabelKey);

/// <summary>The second measure of a kpi widget.</summary>
/// <param name="Measure">The measure, by the name the data source declares.</param>
/// <param name="LabelKey">The text key of its label.</param>
public sealed record KpiComparisonMetadata(string Measure, string LabelKey);

/// <summary>
/// The entity actions a user may take on a widget's entity, and the fields each write action covers.
/// This only shapes the UI: the server checks every request.
/// </summary>
/// <param name="Actions">The granted entity actions, in the order <c>read</c>, <c>create</c>, <c>update</c>, <c>delete</c>.</param>
/// <param name="CreateFields">The fields a create may write, in declaration order. Empty without <c>create</c>.</param>
/// <param name="UpdateFields">The fields an update may write, in declaration order. Empty without <c>update</c>.</param>
public sealed record WidgetAccess(IReadOnlyList<string> Actions, IReadOnlyList<string> CreateFields, IReadOnlyList<string> UpdateFields);

/// <summary>
/// One action of a widget: a <see cref="StartProcessActionMetadata"/>, a <see cref="NavigateActionMetadata"/> or a <see cref="DeleteActionMetadata"/>.
/// Its JSON is that of the runtime type, and every kind writes <c>type</c> first.
/// </summary>
[JsonConverter(typeof(ActionMetadataConverter))]
public abstract record ActionMetadata
{
    /// <summary>The kind of action as written in page files: <c>startProcess</c>, <c>navigate</c> or <c>delete</c>.</summary>
    public abstract string Type { get; init; }

    /// <summary>
    /// The boolean expression over the row that decides whether a row action shows, or null when it always
    /// shows. It is written last. A client works out only part of the expression language and hides the
    /// button when it cannot work the expression out.
    /// </summary>
    [JsonPropertyOrder(1)]
    public string? VisibleWhen { get; init; }
}

/// <summary>Writes an action by its runtime type, so each kind has only its own properties.</summary>
public sealed class ActionMetadataConverter : JsonConverter<ActionMetadata>
{
    public override ActionMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Page metadata is only written.");

    public override void Write(Utf8JsonWriter writer, ActionMetadata value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

/// <summary>A <c>startProcess</c> action of a form widget, which starts a process for the form's record.</summary>
/// <param name="Process">The process, by its declared name.</param>
/// <param name="LabelKey">The text key of the button's label.</param>
/// <param name="InstancesPath">The path of the process start endpoint, so a client never builds it.</param>
public sealed record StartProcessActionMetadata(string Process, string LabelKey, string InstancesPath) : ActionMetadata
{
    [JsonPropertyOrder(-1)]
    public override string Type { get; init; } = "startProcess";
}

/// <summary>A <c>navigate</c> action, which opens another page.</summary>
/// <param name="Page">The target page, by its declared name.</param>
/// <param name="LabelKey">The text key of the button's label.</param>
/// <param name="Parameters">The expression of each target page parameter the action gives, by the name the target declares. Empty when it gives none.</param>
/// <param name="Record">The expression of the record id a form page opens, or null.</param>
/// <param name="OpensForm">Whether the target page holds a form widget.</param>
/// <param name="Paths">The record API path of the target entity of each reference along a path the expressions read, by the declared names up to that reference, such as <c>department</c> for <c>department.name</c>. Empty when the action uses no path.</param>
public sealed record NavigateActionMetadata(
    string Page,
    string LabelKey,
    IReadOnlyDictionary<string, string> Parameters,
    string? Record,
    bool OpensForm,
    IReadOnlyDictionary<string, string> Paths) : ActionMetadata
{
    [JsonPropertyOrder(-1)]
    public override string Type { get; init; } = "navigate";
}

/// <summary>A <c>delete</c> row action of a table, which deletes the row's record after a confirmation.</summary>
/// <param name="LabelKey">The text key of the button's label.</param>
/// <param name="RecordsPath">The path of the record API of the table's entity, or of its data source's root entity, so a client never builds it.</param>
public sealed record DeleteActionMetadata(string LabelKey, string RecordsPath) : ActionMetadata
{
    [JsonPropertyOrder(-1)]
    public override string Type { get; init; } = "delete";
}

/// <summary>The form a form widget lays its entity's fields out with: its sections and its wizard steps in display order.</summary>
/// <param name="Steps">The wizard steps in order. Empty when the form declares none.</param>
public sealed record FormMetadata(string Name, IReadOnlyList<FormSectionMetadata> Sections, IReadOnlyList<FormStepMetadata> Steps);

/// <summary>One section of a form: its name, the text key of its title and its fields in display order.</summary>
/// <param name="Name">The section's name, which a step lists. Null when the section has none.</param>
/// <param name="VisibleWhen">
/// Whether the section has a <c>visibleWhen</c> condition. The condition itself never leaves the
/// server: a client gets the answer from the form state endpoint.
/// </param>
public sealed record FormSectionMetadata(string? Name, string TitleKey, bool VisibleWhen, IReadOnlyList<FormFieldMetadata> Fields);

/// <summary>One field of a form section, by its declared name.</summary>
/// <param name="ReadOnly">
/// Whether a client shows the field without letting it be changed, and never sends it. A computed,
/// numbered or creator field is always read-only.
/// </param>
/// <param name="VisibleWhen">Whether the field has a <c>visibleWhen</c> condition, which stays on the server.</param>
public sealed record FormFieldMetadata(string Name, bool ReadOnly, bool VisibleWhen);

/// <summary>One wizard step of a form: the text key of its title and the names of its sections.</summary>
public sealed record FormStepMetadata(string TitleKey, IReadOnlyList<string> Sections);

/// <summary>The entity a widget shows, with its fields and the path of its record API.</summary>
public sealed record EntityMetadata(string Name, string? LabelKey, string? DisplayField, string RecordsPath, IReadOnlyList<FieldMetadata> Fields);

/// <summary>
/// The data source a table widget shows: the path of its rows endpoint, its root entity, its
/// parameters, its default page size and its columns. The columns are the projected fields in
/// declaration order or, for a grouped data source, the group fields in <c>groupBy</c> order and then
/// the measures.
/// </summary>
public sealed record DataSourceMetadata(
    string Name,
    string RowsPath,
    string Entity,
    IReadOnlyList<DataSourceParameterMetadata> Parameters,
    int PageSize,
    IReadOnlyList<DataSourceColumnMetadata> Columns);

/// <summary>
/// One typed input of a data source's filter. <see cref="Values"/> is set for enum and
/// <see cref="Target"/> for reference.
/// </summary>
/// <param name="Type">The type name as written in data source files, such as <c>date-time</c>.</param>
public sealed record DataSourceParameterMetadata(
    string Name,
    string Type,
    bool Required,
    string? LabelKey,
    IReadOnlyList<string>? Values,
    ReferenceTarget? Target);

/// <summary>
/// One column of a data source: a projected field or, for a grouped data source, a group field or a
/// measure. A field column's type, label key, values and target are those of the field its path ends
/// at. <see cref="Values"/> is set for enum and <see cref="Target"/> for reference. A measure column
/// has the type of its result and no label key, values or target, so its header is its name.
/// </summary>
/// <param name="Name">The projected name: the column's key in a row.</param>
public sealed record DataSourceColumnMetadata(
    string Name,
    string Type,
    string? LabelKey,
    IReadOnlyList<string>? Values,
    ReferenceTarget? Target);

/// <summary>
/// One field. Properties the field's type does not have are null: <see cref="MaxLength"/> is for
/// text, <see cref="Precision"/> and <see cref="Scale"/> for decimal, <see cref="Values"/> for enum,
/// <see cref="Target"/> for reference and <see cref="Fields"/> for child collection.
/// </summary>
/// <param name="Type">The type name as written in entity files, such as <c>date-time</c>.</param>
/// <param name="Computed">Whether the server computes the value. A client shows it and never sends it.</param>
/// <param name="Sequence">Whether a sequence numbers the field on create. A client shows it and never sends it.</param>
/// <param name="Creator">Whether the server sets the field to the creating user's id. A client shows it and never sends it.</param>
/// <param name="Fields">The child entity's fields in declaration order, for a child collection.</param>
public sealed record FieldMetadata(
    string Name,
    string Type,
    string? LabelKey,
    bool Required,
    bool Unique,
    bool Computed,
    bool Sequence,
    bool Creator,
    int? MaxLength,
    int? Precision,
    int? Scale,
    IReadOnlyList<string>? Values,
    ReferenceTarget? Target,
    IReadOnlyList<FieldMetadata>? Fields);

/// <summary>The entity a reference field points to, its display field and the path of its record API.</summary>
public sealed record ReferenceTarget(string Entity, string? DisplayField, string RecordsPath);
