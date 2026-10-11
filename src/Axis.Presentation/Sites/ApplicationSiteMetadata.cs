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

/// <summary>What the SPA needs to render a page: its title and its widgets.</summary>
public sealed record PageMetadata(string Name, string TitleKey, IReadOnlyList<WidgetMetadata> Widgets);

/// <summary>
/// One widget. On a table or form widget, exactly one of <see cref="Entity"/> and
/// <see cref="DataSource"/> is set: the widget shows all records of an entity, or the rows of a data
/// source. <see cref="FormPage"/> is set only on a table widget that names the page holding the form
/// for its records. <see cref="Form"/> is set only on a form widget that names a form; <see cref="Entity"/>
/// is then the form's entity, with every field the user may read, so a client knows the type of each
/// field the form shows. <see cref="TasksPath"/> is set only on a task inbox, which has no entity or data source.
/// <see cref="Actions"/> is empty on every widget but a form widget that declares actions.
/// <see cref="Access"/> is set on a table or form widget and null on a task inbox.
/// </summary>
/// <param name="TasksPath">The path of the application's task API, which lists the signed-in user's open tasks.</param>
/// <param name="Actions">The buttons a form widget shows on a saved record, in page file order.</param>
/// <param name="Access">
/// What the user may do with the widget's entity, or with the root entity of its data source.
/// </param>
public sealed record WidgetMetadata(
    string Type,
    string? FormPage,
    EntityMetadata? Entity,
    DataSourceMetadata? DataSource,
    FormMetadata? Form,
    string? TasksPath,
    IReadOnlyList<ActionMetadata> Actions,
    WidgetAccess? Access);

/// <summary>
/// The entity actions a user may take on a widget's entity, and the fields each write action covers.
/// This only shapes the UI: the server checks every request.
/// </summary>
/// <param name="Actions">The granted entity actions, in the order <c>read</c>, <c>create</c>, <c>update</c>, <c>delete</c>.</param>
/// <param name="CreateFields">The fields a create may write, in declaration order. Empty without <c>create</c>.</param>
/// <param name="UpdateFields">The fields an update may write, in declaration order. Empty without <c>update</c>.</param>
public sealed record WidgetAccess(IReadOnlyList<string> Actions, IReadOnlyList<string> CreateFields, IReadOnlyList<string> UpdateFields);

/// <summary>A <c>startProcess</c> action of a form widget, which starts a process for the form's record.</summary>
/// <param name="Type">The kind of action: always <c>startProcess</c>.</param>
/// <param name="Process">The process, by its declared name.</param>
/// <param name="LabelKey">The text key of the button's label.</param>
/// <param name="InstancesPath">The path of the process start endpoint, so a client never builds it.</param>
public sealed record ActionMetadata(string Type, string Process, string LabelKey, string InstancesPath);

/// <summary>The form a form widget lays its entity's fields out with: its sections in display order.</summary>
public sealed record FormMetadata(string Name, IReadOnlyList<FormSectionMetadata> Sections);

/// <summary>One section of a form: the text key of its title and its fields in display order.</summary>
public sealed record FormSectionMetadata(string TitleKey, IReadOnlyList<FormFieldMetadata> Fields);

/// <summary>One field of a form section, by its declared name.</summary>
/// <param name="ReadOnly">
/// Whether a client shows the field without letting it be changed, and never sends it. A computed,
/// numbered or creator field is always read-only.
/// </param>
public sealed record FormFieldMetadata(string Name, bool ReadOnly);

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
