namespace Axis.Configuration.Resources;

/// <summary>A navigation entry of a site: the page it opens, the text key of its label and optionally an icon from Axis's list.</summary>
public sealed record NavigationEntry(string Page, TextReference Label, string? Icon = null);
