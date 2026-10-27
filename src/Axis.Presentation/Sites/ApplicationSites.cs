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

    public static ApplicationSite Describe(ApplicationModel application, SiteModel site)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(site);

        return new ApplicationSite(
            site.Path,
            site.Title.TextKey,
            new SiteLocales(site.Locales.Default, site.Locales.Fallback, site.Locales.Available),
            [.. site.Navigation.Select(entry => new SiteNavigationItem(entry.Page.Name, entry.Label.TextKey))],
            $"/api/apps/{application.Manifest.Name}/notifications");
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
            : new PageMetadata(
                page.Name,
                page.Title.TextKey,
                [.. page.Parameters.Select(parameter => new PageParameterMetadata(
                    parameter.Name,
                    FieldTypes.Name(parameter.Type),
                    parameter.Required,
                    parameter.Values,
                    parameter.Target is { } target ? Target(application, target.Name) : null))],
                [.. page.Widgets.Select(widget => Widget(application, widget))]);
    }

    private static TextResource? FindText(ApplicationModel application, string locale) =>
        application.Texts.FirstOrDefault(text => string.Equals(text.Locale, locale, StringComparison.OrdinalIgnoreCase));

    private static PageWidgetMetadata Widget(ApplicationModel application, WidgetModel widget) =>
        widget.Type switch
        {
            WidgetType.Columns => new ColumnsWidgetMetadata(
                [.. widget.Columns.Select(column => new ColumnMetadata(column.Width, [.. column.Widgets.Select(child => Widget(application, child))]))]),
            WidgetType.Tabs => new TabsWidgetMetadata(
                [.. widget.Tabs.Select(tab => new TabMetadata(tab.Label.TextKey, [.. tab.Widgets.Select(child => Widget(application, child))]))]),
            WidgetType.Section => new SectionWidgetMetadata(
                widget.Title!.TextKey,
                widget.Collapsed,
                [.. widget.Widgets.Select(child => Widget(application, child))]),
            _ => Leaf(application, widget),
        };

    private static WidgetMetadata Leaf(ApplicationModel application, WidgetModel widget)
    {
        // A delete row action carries the records path of the table's entity, or of its data source's root entity.
        // A navigate action reads its paths from that entity too.
        var rowEntity = WidgetEntity(application, widget);
        return new(
            WidgetTypes.Name(widget.Type),
            widget.Entity is { } entity ? DescribeEntity(application, entity.Name) : null,
            widget.DataSource is { } dataSource ? DataSource(application, dataSource.Name) : null,
            widget.Form is { } form ? DescribeForm(application, form.Name) : null,
            widget.Type == WidgetType.TaskInbox ? $"/api/apps/{application.Manifest.Name}/tasks" : null,
            widget.Process?.Name,
            widget.Process is { } process ? $"/api/apps/{application.Manifest.Name}/processes/{process.Name}/instances" : null,
            widget.Process is { } deadLetterProcess ? $"/api/apps/{application.Manifest.Name}/processes/{deadLetterProcess.Name}/dead-letters" : null,
            [.. widget.Actions.Select(action => Action(application, action, rowEntity))],
            [.. widget.RowActions.Select(action => Action(application, action, rowEntity))],
            [.. widget.ToolbarActions.Select(action => Action(application, action, null))],
            widget.Parameters,
            FullAccess(application, widget),
            widget.Detail is { } detail ? new DetailMetadata(detail.Record) : null,
            widget.Kpi is { } kpi
                ? new KpiMetadata(
                    kpi.Measure,
                    kpi.Label.TextKey,
                    kpi.Comparison is { } comparison ? new KpiComparisonMetadata(comparison.Measure, comparison.Label.TextKey) : null,
                    kpi.Navigate is { } navigate ? (NavigateActionMetadata)Action(application, navigate, null) : null)
                : null,
            widget.Chart is { } chart
                ? new ChartMetadata(
                    chart.Kind.ToString().ToLowerInvariant(),
                    chart.Title.TextKey,
                    chart.Category,
                    [.. chart.Measures.Select(measure => new ChartMeasureMetadata(measure.Name, measure.Label.TextKey))])
                : null);
    }

    private static ActionMetadata Action(ApplicationModel application, WidgetActionModel action, EntityModel? entity) =>
        action switch
        {
            // The compiler allows a delete only in a table's row actions, and a table has an entity or a data source.
            DeleteActionModel delete => new DeleteActionMetadata(delete.Label.TextKey, RecordsPath(application, entity!.Name))
            {
                VisibleWhen = delete.VisibleWhen,
            },

            StartProcessActionModel start => new StartProcessActionMetadata(
                start.Process.Name,
                start.Label.TextKey,
                $"/api/apps/{application.Manifest.Name}/processes/{start.Process.Name}/instances")
            {
                VisibleWhen = start.VisibleWhen,
            },

            // The compiler resolves every navigate target, so the lookup always succeeds.
            NavigateActionModel navigate => new NavigateActionMetadata(
                navigate.Page.Name,
                navigate.Label.TextKey,
                navigate.Parameters,
                navigate.Record,
                application.Pages.First(page => page.Name == navigate.Page.Name).Leaves().Any(leaf => leaf.Type == WidgetType.Form),
                PathRecords(application, navigate, entity))
            {
                VisibleWhen = navigate.VisibleWhen,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    /// <summary>
    /// The record API of each reference along the paths of a navigate action, by the dotted declared
    /// names up to the reference, such as <c>customer</c> and <c>customer.parent</c>. Empty when the
    /// action uses no path.
    /// </summary>
    private static Dictionary<string, string> PathRecords(ApplicationModel application, NavigateActionModel navigate, EntityModel? entity)
    {
        var records = new Dictionary<string, string>();
        if (entity is null)
        {
            return records;
        }

        foreach (var path in navigate.Paths)
        {
            // The compiler resolves every path, so the lookups always succeed.
            var names = path.Split('.');
            var current = entity;
            for (var index = 0; index < names.Length - 1; index++)
            {
                if (!current.TryGetField(names[index], out var field) || field.Target is not { } target
                    || application.FindEntity(target.Id) is not { } next)
                {
                    break;
                }

                records[string.Join('.', names[..(index + 1)])] = RecordsPath(application, next.Name);
                current = next;
            }
        }

        return records;
    }

    /// <summary>
    /// The entity a table or form widget is about: its own entity, or the root entity of its data
    /// source. Null on a task inbox and a process inspector.
    /// </summary>
    public static EntityModel? WidgetEntity(ApplicationModel application, WidgetModel widget)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(widget);

        // The compiler resolves every widget entity and data source, so the lookups always succeed.
        if (widget.Entity is { } entity)
        {
            return application.FindEntity(entity.Id);
        }

        return widget.DataSource is { } reference && application.TryGetDataSource(reference.Name, out var dataSource)
            ? application.FindEntity(dataSource.Entity.Id)
            : null;
    }

    // Without a user to shape it, access lists every entity action and every field. A kpi reads a total
    // row of its data source, not records, and a chart reads its groups, so neither has any. A table
    // lists export after delete, and a table over an entity lists import last.
    private static WidgetAccess? FullAccess(ApplicationModel application, WidgetModel widget)
    {
        if (widget.Type is WidgetType.Kpi or WidgetType.Chart || WidgetEntity(application, widget) is not { } entity)
        {
            return null;
        }

        IReadOnlyList<string> fields = [.. entity.Fields.Select(field => field.Name)];
        IReadOnlyList<string> actions = widget.Type != WidgetType.Table
            ? ["read", "create", "update", "delete"]
            : widget.Entity is not null
                ? ["read", "create", "update", "delete", "export", "import"]
                : ["read", "create", "update", "delete", "export"];
        return new WidgetAccess(actions, fields, fields);
    }

    /// <summary>
    /// The metadata of the form named <paramref name="formName"/>, as a form widget's page metadata
    /// gives it. The name must be one the model resolved, such as a widget's or a task step's form.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model has no form with this name.</exception>
    public static FormMetadata DescribeForm(ApplicationModel application, string formName)
    {
        ArgumentNullException.ThrowIfNull(application);

        // The compiler resolves every widget form, every task form and every form field, so the lookups always succeed.
        if (!application.TryGetForm(formName, out var form))
        {
            throw new InvalidOperationException($"The form '{formName}' is not in the model.");
        }

        var entity = application.FindEntity(form.Entity.Id)
            ?? throw new InvalidOperationException($"The form entity '{form.Entity.Name}' is not in the model.");
        return new FormMetadata(
            form.Name,
            [.. form.Sections.Select(section => new FormSectionMetadata(
                section.Name,
                section.Title.TextKey,
                section.VisibleWhen is not null,
                [.. section.Fields.Select(entry =>
                {
                    entity.TryGetField(entry.Field, out var field);
                    return new FormFieldMetadata(
                        field!.Name,
                        entry.ReadOnly || field.IsComputed || field.Sequence is not null || field.Creator,
                        entry.VisibleWhen is not null);
                })]))],
            [.. form.Steps.Select(step => new FormStepMetadata(step.Title.TextKey, step.Sections))]);
    }

    /// <summary>
    /// <paramref name="form"/> with only the fields in <paramref name="readable"/>. A section left
    /// with no field is dropped, and so is its name from each step. A step left with no section is
    /// dropped.
    /// </summary>
    public static FormMetadata Readable(FormMetadata form, IReadOnlySet<string> readable)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(readable);

        var sections = form.Sections
            .Select(section => section with { Fields = [.. section.Fields.Where(field => readable.Contains(field.Name))] })
            .Where(section => section.Fields.Count > 0)
            .ToList();
        var names = new HashSet<string>(sections.Select(section => section.Name).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        return form with
        {
            Sections = sections,
            Steps = [.. form.Steps
                .Select(step => step with { Sections = [.. step.Sections.Where(names.Contains)] })
                .Where(step => step.Sections.Count > 0)],
        };
    }

    /// <summary>
    /// The metadata of the entity named <paramref name="entityName"/>, as a widget's page metadata
    /// gives it. The name must be one the model resolved, such as a widget's or a form's entity.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model has no entity with this name.</exception>
    public static EntityMetadata DescribeEntity(ApplicationModel application, string entityName)
    {
        ArgumentNullException.ThrowIfNull(application);

        // The compiler resolves every widget entity and every form entity, so the lookup always succeeds.
        if (!application.TryGetEntity(entityName, out var entity))
        {
            throw new InvalidOperationException($"The entity '{entityName}' is not in the model.");
        }

        return new EntityMetadata(
            entity.Name,
            entity.Label?.TextKey,
            entity.DisplayField,
            RecordsPath(application, entity.Name),
            [.. entity.Fields.Select(field => Field(application, field, UploadBase(application, entity.Name)))]);
    }

    /// <summary>
    /// The metadata of the data source named <paramref name="dataSourceName"/>, as a widget's page
    /// metadata gives it. The name must be one the model resolved, such as a widget's.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model has no data source with this name.</exception>
    public static DataSourceMetadata DescribeDataSource(ApplicationModel application, string dataSourceName)
    {
        ArgumentNullException.ThrowIfNull(application);

        return DataSource(application, dataSourceName);
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
                LabelKeys(parameter.Values, parameter.ValueLabels),
                parameter.Target is { } target ? Target(application, target.Name) : null))],
            dataSource.PageSize,
            dataSource.Search,
            // A grouped row holds the group fields, then the measures, not the projected fields.
            dataSource.Aggregate is { } aggregate
                ? [.. aggregate.GroupBy.Select(field => Column(application, field, aggregate.GroupBy)), .. aggregate.Measures.Select(measure => Measure(aggregate, measure))]
                : [.. dataSource.Fields.Select(field => Column(application, field, dataSource.Fields))]);
    }

    // A money column names its fixed currency or the sibling column that holds it.
    private static DataSourceColumnMetadata Column(
        ApplicationModel application, DataSourceFieldModel column, IEnumerable<DataSourceFieldModel> siblings) =>
        new(
            column.Name,
            FieldTypes.Name(column.ValueType),
            column.Field.Label?.TextKey,
            column.Field.Values,
            LabelKeys(column.Field.Values, column.Field.ValueLabels),
            column.Field.Type == FieldType.Reference
                ? new ReferenceTarget(column.Field.Target!.Name, column.Field.TargetDisplayField, RecordsPath(application, column.Field.Target.Name))
                : null,
            Currency: column.Field.Type == FieldType.Money ? column.Field.Currency : null,
            CurrencyColumn: column.Field is { Type: FieldType.Money, CurrencyField: { } currencyField }
                ? CurrencyColumnOf(siblings, column, currencyField)?.Name
                : null);

    // A measure has no label, so its header is its name. A count or a count of distinct values is an
    // integer. A sum or average of integers or decimals is written like a decimal, and a sum or average of
    // money stays money. A min or max keeps the type of its field. Only a money measure names a currency,
    // which its fixed currency or the group column that holds it gives.
    private static DataSourceColumnMetadata Measure(DataSourceAggregateModel aggregate, DataSourceMeasureModel measure)
    {
        var field = measure.Field?.Field;
        var type = measure.Function switch
        {
            AggregateFunction.Count or AggregateFunction.CountDistinct => FieldTypes.Name(FieldType.Integer),
            AggregateFunction.Sum or AggregateFunction.Avg when !FieldTypes.HoldsDecimal(field!.Type) => FieldTypes.Name(FieldType.Decimal),
            _ => FieldTypes.Name(field!.Type),
        };
        if (field is null || type != FieldTypes.Name(FieldType.Money))
        {
            return new(measure.Name, type, null, null, null, null);
        }

        return field.CurrencyField is null
            ? new(measure.Name, type, null, null, null, null, Currency: field.Currency)
            : new(measure.Name, type, null, null, null, null, CurrencyColumn: CurrencyColumnOf(aggregate.GroupBy, measure.Field!, field.CurrencyField)?.Name);
    }

    // The column that reads the currency field on the same record as the money field: the same
    // reference fields, then the currency field. The model holds resolved names, so they compare exactly.
    private static DataSourceFieldModel? CurrencyColumnOf(
        IEnumerable<DataSourceFieldModel> columns, DataSourceFieldModel money, string currencyField) =>
        columns.FirstOrDefault(group =>
            group.Path.Count == money.Path.Count
            && string.Equals(group.Field.Name, currencyField, StringComparison.Ordinal)
            && group.Path.Take(group.Path.Count - 1).Zip(money.Path, (left, right) => string.Equals(left.Name, right.Name, StringComparison.Ordinal)).All(same => same));

    // A parameter's model holds only its target entity, so the display field comes from that entity.
    private static ReferenceTarget Target(ApplicationModel application, string entityName)
    {
        if (!application.TryGetEntity(entityName, out var entity))
        {
            throw new InvalidOperationException($"The parameter target '{entityName}' is not in the model.");
        }

        return new ReferenceTarget(entity.Name, entity.DisplayField, RecordsPath(application, entity.Name));
    }

    // An entity of a module is named {module}/{entity} and has no upload route yet.
    private static string? UploadBase(ApplicationModel application, string entityName) =>
        entityName.Contains('/', StringComparison.Ordinal) ? null : $"/api/apps/{application.Manifest.Name}/entities/{entityName}/fields";

    private static FieldMetadata Field(ApplicationModel application, FieldModel field, string? uploadBase) =>
        new(
            field.Name,
            FieldTypes.Name(field.Type),
            field.Label?.TextKey,
            field.Required,
            field.Unique,
            field.IsComputed,
            field.Sequence is not null,
            field.Creator,
            field.MaxLength,
            field.Precision,
            field.Scale,
            field.Currency,
            field.CurrencyField,
            field.Min,
            field.Max,
            field.MinLength,
            field.Values,
            LabelKeys(field.Values, field.ValueLabels),
            field.Type == FieldType.File ? field.MaxSize : null,
            field.Type == FieldType.File ? field.Accept : null,
            field.Type == FieldType.File && uploadBase is not null ? $"{uploadBase}/{field.Name}/files" : null,
            field.Type == FieldType.File ? $"/api/apps/{application.Manifest.Name}/files" : null,
            field.Type is FieldType.Reference or FieldType.References
                ? new ReferenceTarget(field.Target!.Name, field.TargetDisplayField, RecordsPath(application, field.Target.Name))
                : null,
            field.Type == FieldType.ChildCollection ? ChildFields(application, field, uploadBase) : null);

    // Keys follow the enum's values, so the JSON order is stable and matches the choice order.
    private static Dictionary<string, string>? LabelKeys(IReadOnlyList<string>? values, IReadOnlyDictionary<string, TextReference>? labels) =>
        values is null || labels is null
            ? null
            : values.Where(labels.ContainsKey).ToDictionary(value => value, value => labels[value].TextKey, StringComparer.Ordinal);

    // A child entity has no child collection fields, so this goes one level deep.
    private static FieldMetadata[] ChildFields(ApplicationModel application, FieldModel collection, string? uploadBase)
    {
        var child = application.FindEntity(collection.Target!.Id)
            ?? throw new InvalidOperationException("The application has no entity for the child collection.");
        var childBase = uploadBase is null ? null : $"{uploadBase}/{collection.Name}";
        return [.. child.Fields.Select(field => Field(application, field, childBase))];
    }

    private static string RecordsPath(ApplicationModel application, string entityName) =>
        $"/api/apps/{application.Manifest.Name}/entities/{entityName}/records";
}
