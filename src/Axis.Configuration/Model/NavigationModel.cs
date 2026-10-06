using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled navigation entry: the page it opens and the text key of its label.</summary>
public sealed record NavigationModel(PageReference Page, TextReference Label);
