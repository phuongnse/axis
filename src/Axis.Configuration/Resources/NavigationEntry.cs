namespace Axis.Configuration.Resources;

/// <summary>A navigation entry of a site: the page it opens and the text key of its label.</summary>
public sealed record NavigationEntry(string Page, TextReference Label);
