using Axis.Configuration.Releases;

namespace Axis.Configuration.Templates;

/// <summary>
/// An installed template version: its manifest and every <c>*.json</c> file of its folder except
/// <c>template.json</c>. Each path is relative to the version folder with <c>/</c> separators, each
/// content is the raw text, and the files are in ordinal path order.
/// </summary>
public sealed record InstalledTemplate(TemplateManifest Manifest, IReadOnlyList<ResourceContent> Files);

/// <summary>A template version that was left out of the catalog. <c>Folder</c> is <c>&lt;id&gt;/&lt;version&gt;</c>.</summary>
public sealed record TemplateProblem(string Folder, string Message);
