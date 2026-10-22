namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>page</c> resource: a route in a site whose content is its widgets. A widget may be a
/// container that holds more widgets, so the widgets form a tree.
/// </summary>
public sealed record PageResource : Resource
{
    public required TextReference Title { get; init; }

    public IReadOnlyList<PageParameterDefinition> Parameters { get; init; } = [];

    public required IReadOnlyList<WidgetDefinition> Widgets { get; init; }
}
