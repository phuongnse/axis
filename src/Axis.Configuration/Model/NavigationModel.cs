using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled navigation entry: the page it opens, the text key of its label and its icon name, or null when it has none.</summary>
public sealed record NavigationModel(PageReference Page, TextReference Label, string? Icon = null);
