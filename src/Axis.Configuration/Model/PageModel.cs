using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled page: a route in a site whose content is its widgets.</summary>
public sealed record PageModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required TextReference Title { get; init; }

    /// <summary>The page's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required IReadOnlyList<WidgetModel> Widgets { get; init; }
}
