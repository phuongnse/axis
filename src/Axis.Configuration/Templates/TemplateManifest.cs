namespace Axis.Configuration.Templates;

/// <summary>
/// The manifest of a template version, <c>template.json</c>. <paramref name="BuiltIn"/> is true when the manifest
/// marks the template as a built-in application.
/// </summary>
public sealed record TemplateManifest(string Id, string Version, TemplateText Title, TemplateText Description, bool BuiltIn = false);

/// <summary>A text of a template manifest in both languages of Studio.</summary>
public sealed record TemplateText(string En, string Vi);
