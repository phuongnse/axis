namespace Axis.Configuration.Templates;

/// <summary>The manifest of a template version, <c>template.json</c>.</summary>
public sealed record TemplateManifest(string Id, string Version, TemplateText Title, TemplateText Description);

/// <summary>A text of a template manifest in both languages of Studio.</summary>
public sealed record TemplateText(string En, string Vi);
