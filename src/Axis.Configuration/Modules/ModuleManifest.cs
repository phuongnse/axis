namespace Axis.Configuration.Modules;

/// <summary>The manifest of a module version, <c>module.json</c>.</summary>
public sealed record ModuleManifest(string Id, string Version, string Label);
