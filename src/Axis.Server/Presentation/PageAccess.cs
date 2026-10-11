using Axis.Configuration.Model;
using Axis.Policy;
using Axis.Presentation.Sites;
using Axis.Server.DataSources;

namespace Axis.Server.Presentation;

/// <summary>
/// Shapes navigation and page metadata by a signed-in user's grants. This only shapes the UI: the
/// record API, the data source endpoint and the process start endpoint still check every request.
/// </summary>
internal static class PageAccess
{
    // The entity actions in the order access lists them, with their names.
    private static readonly (PolicyAction Action, string Name)[] _entityActions =
        [(PolicyAction.Read, "read"), (PolicyAction.Create, "create"), (PolicyAction.Update, "update"), (PolicyAction.Delete, "delete")];

    /// <summary>
    /// Whether <paramref name="roles"/> may read every widget of <paramref name="page"/>. A task
    /// inbox is always readable. A widget over an entity or a form needs a <c>read</c> grant on the
    /// entity, and one over a data source needs what the data source endpoint needs.
    /// </summary>
    public static bool CanRead(ApplicationModel application, IReadOnlyList<string> roles, PageModel page) =>
        page.Widgets.All(widget => CanRead(application, roles, widget));

    /// <summary>
    /// <paramref name="metadata"/>, the metadata of <paramref name="page"/>, with only the
    /// <c>startProcess</c> actions <paramref name="roles"/> may start and the access they hold.
    /// </summary>
    public static PageMetadata Shape(ApplicationModel application, IReadOnlyList<string> roles, PageModel page, PageMetadata metadata) =>
        metadata with
        {
            Widgets = [.. page.Widgets.Zip(metadata.Widgets, (widget, described) => described with
            {
                Actions = [.. described.Actions.Where(action =>
                    application.TryGetProcess(action.Process, out var process) && PolicyEvaluator.AllowsStart(application, roles, process))],
                Access = ApplicationSites.WidgetEntity(application, widget) is { } entity ? Access(application, roles, entity) : null,
            })],
        };

    private static bool CanRead(ApplicationModel application, IReadOnlyList<string> roles, WidgetModel widget)
    {
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
