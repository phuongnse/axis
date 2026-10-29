using Axis.Configuration.Resources;

namespace Axis.Configuration.Modules;

/// <summary>
/// The manifest of a module version, <c>module.json</c>. <see cref="Modules"/> lists the modules it
/// depends on, with the entries of an application's <c>modules</c>.
/// </summary>
public sealed record ModuleManifest(string Id, string Version, string Label)
{
    public IReadOnlyList<ApplicationModuleDefinition> Modules { get; init; } = [];
}
