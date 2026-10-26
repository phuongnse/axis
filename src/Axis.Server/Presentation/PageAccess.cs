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
    /// that remain have only the fields the roles may read, the <c>startProcess</c> actions they may start, the
    /// <c>delete</c> actions they hold a <c>delete</c> grant for, the <c>navigate</c> actions, and the <c>navigate</c> of a kpi, to pages they may read and the access they hold.
    /// A <c>navigate</c> action whose paths pass a field the roles may not read, or an entity they may not read, is left out too.
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
            Form = readable is null || described.Form is null ? described.Form : ApplicationSites.Readable(described.Form, readable),
            Actions = ShapeActions(application, roles, entity, described.Actions, widget.Actions),
            RowActions = ShapeActions(application, roles, entity, described.RowActions, widget.RowActions),
            ToolbarActions = ShapeActions(application, roles, entity, described.ToolbarActions, widget.ToolbarActions),
            Access = widget.Type is WidgetType.Kpi or WidgetType.Chart || entity is null ? null : Access(application, roles, widget, entity),
            Kpi = described.Kpi is { Navigate: { } navigate } kpi && !Allows(application, roles, null, navigate) ? kpi with { Navigate = null } : described.Kpi,
        };
    }

    // The model and the metadata hold the same actions, so their lists pair up by index. A navigate
    // action is also left out when the roles may not read a field along one of its paths.
    private static List<ActionMetadata> ShapeActions(
        ApplicationModel application,
        IReadOnlyList<string> roles,
        EntityModel? entity,
        IReadOnlyList<ActionMetadata> described,
        IReadOnlyList<WidgetActionModel> actions) =>
        [.. described
            .Zip(actions)
            .Where(pair => Allows(application, roles, entity, pair.First)
                && (pair.Second is not NavigateActionModel navigate || ReadsPaths(application, roles, entity, navigate)))
            .Select(pair => pair.First)];

    // Every field along each path must be readable on the entity it belongs to, the first field on
    // the widget's entity included, the same check as for a data source's fields. An entity with no
    // read grant has an empty set, so no field of it is readable.
    private static bool ReadsPaths(ApplicationModel application, IReadOnlyList<string> roles, EntityModel? entity, NavigateActionModel navigate)
    {
        foreach (var path in navigate.Paths)
        {
            var current = entity;
            foreach (var name in path.Split('.'))
            {
                // The compiler resolves every path, so the lookups always succeed.
                if (current is null)
                {
                    return false;
                }

                if (PolicyEvaluator.Fields(application, roles, current, PolicyAction.Read) is { } readable && !readable.Contains(name))
                {
                    return false;
                }

                current = current.TryGetField(name, out var field) && field.Target is { } target ? application.FindEntity(target.Id) : null;
            }
        }

        return true;
    }

    private static bool Allows(ApplicationModel application, IReadOnlyList<string> roles, EntityModel? entity, ActionMetadata action) =>
        action switch
        {
            // The entity is the table's own, or its data source's root. Only a table has a delete action.
            DeleteActionMetadata => entity is not null && PolicyEvaluator.Access(application, roles, entity, PolicyAction.Delete).Granted,

            StartProcessActionMetadata start =>
                application.TryGetProcess(start.Process, out var process) && PolicyEvaluator.AllowsStart(application, roles, process),

            // The compiler resolves every navigate target, so the lookup always succeeds. The metadata holds the declared name.
            NavigateActionMetadata navigate =>
                CanRead(application, roles, application.Pages.First(page => page.Name == navigate.Page)),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    // A child collection field covers its rows as a whole, so it is kept with all its fields or
    // left out.
    private static EntityMetadata? Readable(EntityMetadata? entity, IReadOnlySet<string> readable) =>
        entity is null ? null : entity with { Fields = [.. entity.Fields.Where(field => readable.Contains(field.Name))] };

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

    // A table over an entity ends its actions with import, for roles that may import and create. A
    // form, a detail and a table over a data source have no import.
    private static WidgetAccess Access(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget, EntityModel entity) =>
        new(
            [.. _entityActions
                .Where(entry => PolicyEvaluator.Access(application, roles, entity, entry.Action).Granted)
                .Select(entry => entry.Name)
                .Concat(widget.Type == WidgetType.Table && widget.Entity is not null && PolicyEvaluator.AllowsImport(application, roles, entity) ? ["import"] : [])],
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
