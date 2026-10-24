using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled <c>navigate</c> action: the page it opens and its button label. <see cref="Parameters"/> holds
/// the expression of each page parameter it gives, by the name the target page declares, in file order.
/// <see cref="Record"/> is the expression of the record id a form page opens, or null.
/// <see cref="Paths"/> lists each reference path those expressions read.
/// </summary>
public sealed record NavigateActionModel(PageReference Page, TextReference Label) : WidgetActionModel(Label)
{
    /// <summary>The shared empty map, so equal actions stay equal.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoParameters = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = NoParameters;

    /// <summary>The shared empty list, so equal actions stay equal.</summary>
    public static readonly IReadOnlyList<string> NoPaths = [];

    /// <summary>
    /// Each reference path the parameters and <see cref="Record"/> read, as dotted declared field
    /// names from the widget's entity, such as <c>department.name</c>. The parameters come in file
    /// order, then the record, without duplicates ignoring letter case.
    /// </summary>
    public IReadOnlyList<string> Paths { get; init; } = NoPaths;

    public string? Record { get; init; }
}
