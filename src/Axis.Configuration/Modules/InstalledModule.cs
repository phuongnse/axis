using Axis.Configuration.Releases;

namespace Axis.Configuration.Modules;

/// <summary>
/// An installed module version: its manifest and every <c>*.json</c> file of its folder, including
/// <c>module.json</c>. Each path is relative to the version folder with <c>/</c> separators, each
/// content is the raw text, and the files are in ordinal path order.
/// </summary>
public sealed record InstalledModule(ModuleManifest Manifest, IReadOnlyList<ResourceContent> Files);
