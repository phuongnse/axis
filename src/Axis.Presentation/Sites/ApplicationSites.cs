using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Presentation.Texts;

namespace Axis.Presentation.Sites;

/// <summary>
/// Describes the sites, texts and pages of a compiled application to the SPA. Names in the results
/// are the model's declared names, never the letter case of a request.
/// </summary>
public static class ApplicationSites
{
    /// <summary>The list entry of <paramref name="site"/>, with its title in each available locale.</summary>
    public static SiteListItem ListItem(ApplicationModel application, SiteModel site)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(site);

        // The compiler guarantees a text resource for each available locale, holding the title key.
        var titles = site.Locales.Available.ToDictionary(
            locale => locale,
            locale => FindText(application, locale)!.Texts[site.Title.TextKey],
            StringComparer.Ordinal);
        return new SiteListItem(site.Path, site.Title.TextKey, titles);
    }

    public static ApplicationSite Describe(SiteModel site)
    {
        ArgumentNullException.ThrowIfNull(site);

        return new ApplicationSite(
            site.Path,
            site.Title.TextKey,
            new SiteLocales(site.Locales.Default, site.Locales.Fallback, site.Locales.Available),
            [.. site.Navigation.Select(entry => new SiteNavigationItem(entry.Page.Name, entry.Label.TextKey))]);
    }

    /// <summary>The texts of <paramref name="locale"/>, ignoring letter case, or null when the application has none.</summary>
    public static TextResources? FindTexts(ApplicationModel application, string locale)
    {
        ArgumentNullException.ThrowIfNull(application);

        return FindText(application, locale) is { } text ? new TextResources(text.Locale, text.Texts) : null;
    }

    /// <summary>The page named <paramref name="pageName"/>, ignoring letter case, or null when the application has none.</summary>
    public static PageMetadata? FindPage(ApplicationModel application, string pageName)
    {
        ArgumentNullException.ThrowIfNull(application);

        var page = application.Pages.FirstOrDefault(candidate => string.Equals(candidate.Name, pageName, StringComparison.OrdinalIgnoreCase));
        return page is null
            ? null
            : new PageMetadata(page.Name, page.Title.TextKey, [.. page.Widgets.Select(widget => Widget(application, widget))]);
    }

    private static TextResource? FindText(ApplicationModel application, string locale) =>
        application.Texts.FirstOrDefault(text => string.Equals(text.Locale, locale, StringComparison.OrdinalIgnoreCase));

    private static WidgetMetadata Widget(ApplicationModel application, WidgetModel widget) =>
        new(
            WidgetTypes.Name(widget.Type),
            widget.FormPage?.Name,
            widget.Entity is { } entity ? Entity(application, entity.Name) : null,
            widget.DataSource is { } dataSource ? DataSource(application, dataSource.Name) : null,
            widget.Form is { } form ? Form(application, form.Name) : null);

    private static FormMetadata Form(ApplicationModel application, string name)
    {
        // The compiler resolves every widget form and every form field, so the lookups always succeed.
        if (!application.TryGetForm(name, out var form))
        {
            throw new InvalidOperationException($"The widget form '{name}' is not in the model.");
        }

        var entity = application.FindEntity(form.Entity.Id)
            ?? throw new InvalidOperationException($"The form entity '{form.Entity.Name}' is not in the model.");
        return new FormMetadata(
            form.Name,
            [.. form.Sections.Select(section => new FormSectionMetadata(
                section.Title.TextKey,
                [.. section.Fields.Select(entry =>
                {
                    entity.TryGetField(entry.Field, out var field);
                    return new FormFieldMetadata(field!.Name, entry.ReadOnly || field.IsComputed || field.Sequence is not null);
                })]))]);
    }

    private static EntityMetadata Entity(ApplicationModel application, string name)
    {
        // The compiler resolves every widget entity, so the lookup always succeeds.
        if (!application.TryGetEntity(name, out var entity))
        {
            throw new InvalidOperationException($"The widget entity '{name}' is not in the model.");
        }

        return new EntityMetadata(
            entity.Name,
            entity.Label?.TextKey,
            entity.DisplayField,
            RecordsPath(application, entity.Name),
            [.. entity.Fields.Select(field => Field(application, field))]);
    }

    private static DataSourceMetadata DataSource(ApplicationModel application, string name)
    {
        // The compiler resolves every widget data source, so the lookup always succeeds.
        if (!application.TryGetDataSource(name, out var dataSource))
        {
            throw new InvalidOperationException($"The widget data source '{name}' is not in the model.");
        }

        return new DataSourceMetadata(
            dataSource.Name,
            $"/api/apps/{application.Manifest.Name}/data-sources/{dataSource.Name}/rows",
            dataSource.Entity.Name,
            [.. dataSource.Parameters.Select(parameter => new DataSourceParameterMetadata(
                parameter.Name,
                FieldTypes.Name(parameter.Type),
                parameter.Required,
                parameter.Label?.TextKey,
                parameter.Values,
                parameter.Target is { } target ? Target(application, target.Name) : null))],
            dataSource.PageSize,
            // A grouped row holds the group fields, then the measures, not the projected fields.
            dataSource.Aggregate is { } aggregate
                ? [.. aggregate.GroupBy.Select(field => Column(application, field)), .. aggregate.Measures.Select(Measure)]
                : [.. dataSource.Fields.Select(field => Column(application, field))]);
    }

    private static DataSourceColumnMetadata Column(ApplicationModel application, DataSourceFieldModel column) =>
        new(
            column.Name,
            FieldTypes.Name(column.Field.Type),
            column.Field.Label?.TextKey,
            column.Field.Values,
            column.Field.Type == FieldType.Reference
                ? new ReferenceTarget(column.Field.Target!.Name, column.Field.TargetDisplayField, RecordsPath(application, column.Field.Target.Name))
                : null);

    // A measure has no label, so its header is its name. A count is an integer and a sum is
    // written like a decimal. A min or max keeps the type of its field.
    private static DataSourceColumnMetadata Measure(DataSourceMeasureModel measure) =>
        new(
            measure.Name,
            measure.Function switch
            {
                AggregateFunction.Count => FieldTypes.Name(FieldType.Integer),
                AggregateFunction.Sum => FieldTypes.Name(FieldType.Decimal),
                _ => FieldTypes.Name(measure.Field!.Field.Type),
            },
            null,
            null,
            null);

    // A parameter's model holds only its target entity, so the display field comes from that entity.
    private static ReferenceTarget Target(ApplicationModel application, string entityName)
    {
        if (!application.TryGetEntity(entityName, out var entity))
        {
            throw new InvalidOperationException($"The parameter target '{entityName}' is not in the model.");
        }

        return new ReferenceTarget(entity.Name, entity.DisplayField, RecordsPath(application, entity.Name));
    }

    private static FieldMetadata Field(ApplicationModel application, FieldModel field) =>
        new(
            field.Name,
            FieldTypes.Name(field.Type),
            field.Label?.TextKey,
            field.Required,
            field.Unique,
            field.IsComputed,
            field.Sequence is not null,
            field.MaxLength,
            field.Precision,
            field.Scale,
            field.Values,
            field.Type == FieldType.Reference
                ? new ReferenceTarget(field.Target!.Name, field.TargetDisplayField, RecordsPath(application, field.Target.Name))
                : null,
            field.Type == FieldType.ChildCollection ? ChildFields(application, field) : null);

    // A child entity has no reference or child collection fields, so this goes one level deep.
    private static FieldMetadata[] ChildFields(ApplicationModel application, FieldModel collection)
    {
        var child = application.FindEntity(collection.Target!.Id)
            ?? throw new InvalidOperationException("The application has no entity for the child collection.");
        return [.. child.Fields.Select(field => Field(application, field))];
    }

    private static string RecordsPath(ApplicationModel application, string entityName) =>
        $"/api/apps/{application.Manifest.Name}/entities/{entityName}/records";
}
