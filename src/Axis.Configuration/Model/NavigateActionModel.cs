using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled <c>navigate</c> action: the page it opens and its button label.</summary>
public sealed record NavigateActionModel(PageReference Page, TextReference Label) : WidgetActionModel(Label);
