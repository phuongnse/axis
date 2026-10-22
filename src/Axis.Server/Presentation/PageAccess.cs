using Axis.Configuration.Model;
using Axis.Policy;
using Axis.Presentation.Sites;
using Axis.Server.DataSources;

namespace Axis.Server.Presentation;

/// <summary>
/// Shapes navigation and page metadata by a signed-in user's grants, down to the fields the user may
/// read. This only shapes the UI: the
/// record API, the data source endpoint and the process start endpoint still check every request.
/// </summary>
internal static class PageAccess
{
    // The entity actions in the order access lists them, with their names.
    private static readonly (PolicyAction Action, string Name)[] _entityActions =
        [(PolicyAction.Read, "read"), (PolicyAction.Create, "create"), (PolicyAction.Update, "update"), (PolicyAction.Delete, "delete")];

    /// <summary>
    /// Whether <paramref name="roles"/> may read at least one widget of <paramref name="page"/>, at any depth.
    /// A task inbox is always readable. A process inspector needs an <c>inspect</c> grant on its process.
    /// A widget over an entity or a form needs a <c>read</c> grant on the entity, and one over a
    /// data source needs what the data source endpoint needs. A container is readable through the widgets it holds.
    /// </summary>
    public static bool CanRead(ApplicationModel application, IReadOnlyList<string> roles, PageModel page) =>
        page.Leaves().Any(widget => CanRead(application, roles, widget));

    /// <summary>
    /// <paramref name="metadata"/>, the metadata of <paramref name="page"/>, without the widgets
    /// <paramref name="roles"/> may not read, and without the columns, tabs and containers that leave empty. The widgets
    /// that remain have only the fields the roles may read, the <c>startProcess</c> actions they may start and the
    /// access they hold.
    /// </summary>
    public static PageMetadata Shape(ApplicationModel application, IReadOnlyList<string> roles, PageModel page, PageMetadata metadata) =>
        metadata with { Widgets = ShapeAll(application, roles, page.Widgets, metadata.Widgets) };

    // The model and the metadata hold the same tree, so their lists pair up by index.
    private static List<PageWidgetMetadata> ShapeAll(
        ApplicationModel application,
        IReadOnlyList<string> roles,
        IReadOnlyList<WidgetModel> widgets,
        IReadOnlyList<PageWidgetMetadata> described) =>
        [.. widgets.Zip(described, (widget, metadata) => Shape(application, roles, widget, metadata)).OfType<PageWidgetMetadata>()];

    private static PageWidgetMetadata? Shape(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget, PageWidgetMetadata described) =>
        described switch
        {
            ColumnsWidgetMetadata columns => ShapeColumns(application, roles, widget, columns),
            TabsWidgetMetadata tabs => ShapeTabs(application, roles, widget, tabs),
            SectionWidgetMetadata section => ShapeSection(application, roles, widget, section),
            WidgetMetadata leaf => CanRead(application, roles, widget) ? ShapeLeaf(application, roles, widget, leaf) : null,
            _ => throw new ArgumentOutOfRangeException(nameof(described), described, null),
        };

    // A column or tab with no widget left is dropped, and the others keep their widths and order.
    private static ColumnsWidgetMetadata? ShapeColumns(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget, ColumnsWidgetMetadata described)
    {
        var columns = widget.Columns
            .Zip(described.Columns, (column, metadata) => metadata with { Widgets = ShapeAll(application, roles, column.Widgets, metadata.Widgets) })
            .Where(column => column.Widgets.Count > 0)
            .ToList();
        return columns.Count == 0 ? null : described with { Columns = columns };
    }

    private static TabsWidgetMetadata? ShapeTabs(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget, TabsWidgetMetadata described)
    {
        var tabs = widget.Tabs
            .Zip(described.Tabs, (tab, metadata) => metadata with { Widgets = ShapeAll(application, roles, tab.Widgets, metadata.Widgets) })
            .Where(tab => tab.Widgets.Count > 0)
            .ToList();
        return tabs.Count == 0 ? null : described with { Tabs = tabs };
    }

    private static SectionWidgetMetadata? ShapeSection(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget, SectionWidgetMetadata described)
    {
        var widgets = ShapeAll(application, roles, widget.Widgets, described.Widgets);
        return widgets.Count == 0 ? null : described with { Widgets = widgets };
    }

    private static WidgetMetadata ShapeLeaf(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget, WidgetMetadata described)
    {
        var entity = ApplicationSites.WidgetEntity(application, widget);

        // Fields gives null for every field. The page answers 403 before shaping when the user has
        // no read grant, so the set is never empty here.
        var readable = entity is null ? null : PolicyEvaluator.Fields(application, roles, entity, PolicyAction.Read);
        return described with
        {
            Entity = readable is null ? described.Entity : Readable(described.Entity, readable),
            Form = readable is null ? described.Form : Readable(described.Form, readable),
            Actions = [.. described.Actions.Where(action =>
                application.TryGetProcess(action.Process, out var process) && PolicyEvaluator.AllowsStart(application, roles, process))],
            Access = entity is null ? null : Access(application, roles, entity),
        };
    }

    // A child collection field covers its rows as a whole, so it is kept with all its fields or
    // left out.
    private static EntityMetadata? Readable(EntityMetadata? entity, IReadOnlySet<string> readable) =>
        entity is null ? null : entity with { Fields = [.. entity.Fields.Where(field => readable.Contains(field.Name))] };

    // A section with no readable field is left out.
    private static FormMetadata? Readable(FormMetadata? form, IReadOnlySet<string> readable) =>
        form is null
            ? null
            : form with
            {
                Sections = [.. form.Sections
                    .Select(section => section with { Fields = [.. section.Fields.Where(field => readable.Contains(field.Name))] })
                    .Where(section => section.Fields.Count > 0)],
            };

    private static bool CanRead(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget)
    {
        if (widget.Process is { } inspected)
        {
            // The compiler resolves every inspected process, so the lookup always succeeds.
            return application.TryGetProcess(inspected.Name, out var process) && PolicyEvaluator.AllowsInspect(application, roles, process);
        }

        if (widget.DataSource is { } reference)
        {
            // The compiler resolves every widget data source, so the lookup always succeeds.
            return application.TryGetDataSource(reference.Name, out var dataSource)
                && PolicyEvaluator.AllowsRead(application, roles, dataSource)
                && DataSourceFieldAccess.Read(application, roles, dataSource) is not null;
        }

        return ApplicationSites.WidgetEntity(application, widget) is not { } entity
            || PolicyEvaluator.Access(application, roles, entity, PolicyAction.Read).Granted;
    }

    private static WidgetAccess Access(ApplicationModel application, IReadOnlyList<string> roles, EntityModel entity) =>
        new(
            [.. _entityActions
                .Where(entry => PolicyEvaluator.Access(application, roles, entity, entry.Action).Granted)
                .Select(entry => entry.Name)],
            Writable(application, roles, entity, PolicyAction.Create),
            Writable(application, roles, entity, PolicyAction.Update));

    // Fields gives null for every field and an empty set without a grant for the action. The
    // fields are listed from the entity, so they keep its declaration order.
    private static string[] Writable(ApplicationModel application, IReadOnlyList<string> roles, EntityModel entity, PolicyAction action)
    {
        var fields = PolicyEvaluator.Fields(application, roles, entity, action);
        return [.. entity.Fields.Select(field => field.Name).Where(name => fields is null || fields.Contains(name))];
    }
}
