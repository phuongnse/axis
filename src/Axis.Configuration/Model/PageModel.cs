using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled page: a route in a site whose content is its widgets. <see cref="Widgets"/> is the top level of a tree,
/// whose containers hold the other widgets.
/// </summary>
public sealed record PageModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required TextReference Title { get; init; }

    /// <summary>The page's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required IReadOnlyList<WidgetModel> Widgets { get; init; }

    /// <summary>The widgets that show something, not the containers around them, depth first in page file order.</summary>
    public IEnumerable<WidgetModel> Leaves() => Leaves(Widgets);

    private static IEnumerable<WidgetModel> Leaves(IEnumerable<WidgetModel> widgets)
    {
        foreach (var widget in widgets)
        {
            switch (widget.Type)
            {
                case WidgetType.Columns:
                    foreach (var leaf in Leaves(widget.Columns.SelectMany(column => column.Widgets)))
                    {
                        yield return leaf;
                    }

                    break;
                case WidgetType.Tabs:
                    foreach (var leaf in Leaves(widget.Tabs.SelectMany(tab => tab.Widgets)))
                    {
                        yield return leaf;
                    }

                    break;
                case WidgetType.Section:
                    foreach (var leaf in Leaves(widget.Widgets))
                    {
                        yield return leaf;
                    }

                    break;
                default:
                    yield return widget;
                    break;
            }
        }
    }
}
