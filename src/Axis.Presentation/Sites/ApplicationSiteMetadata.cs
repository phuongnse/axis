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
/// One widget. <see cref="FormPage"/> is set only on a table widget that names the page holding the
/// form for its records.
/// </summary>
public sealed record WidgetMetadata(string Type, string? FormPage, EntityMetadata Entity);

/// <summary>The entity a widget shows, with its fields and the path of its record API.</summary>
public sealed record EntityMetadata(string Name, string? LabelKey, string? DisplayField, string RecordsPath, IReadOnlyList<FieldMetadata> Fields);

/// <summary>
/// One field. Properties the field's type does not have are null: <see cref="MaxLength"/> is for
/// text, <see cref="Precision"/> and <see cref="Scale"/> for decimal, <see cref="Values"/> for enum
/// and <see cref="Target"/> for reference.
/// </summary>
/// <param name="Type">The type name as written in entity files, such as <c>date-time</c>.</param>
public sealed record FieldMetadata(
    string Name,
    string Type,
    string? LabelKey,
    bool Required,
    bool Unique,
    int? MaxLength,
    int? Precision,
    int? Scale,
    IReadOnlyList<string>? Values,
    ReferenceTarget? Target);

/// <summary>The entity a reference field points to, its display field and the path of its record API.</summary>
public sealed record ReferenceTarget(string Entity, string? DisplayField, string RecordsPath);
