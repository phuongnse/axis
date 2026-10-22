using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled <c>navigate</c> action: the page it opens and its button label. <see cref="Parameters"/> holds
/// the expression of each page parameter it gives, by the name the target page declares, in file order.
/// <see cref="Record"/> is the expression of the record id a form page opens, or null.
/// </summary>
public sealed record NavigateActionModel(PageReference Page, TextReference Label) : WidgetActionModel(Label)
{
    /// <summary>The shared empty map, so equal actions stay equal.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoParameters = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = NoParameters;

    public string? Record { get; init; }
}
