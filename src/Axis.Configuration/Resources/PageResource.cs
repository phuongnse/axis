namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>page</c> resource: a route in a site whose content is its widgets. A page has exactly one
/// widget for now.
/// </summary>
public sealed record PageResource : Resource
{
    public required TextReference Title { get; init; }

    public required IReadOnlyList<WidgetDefinition> Widgets { get; init; }
}
