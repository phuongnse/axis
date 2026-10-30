using Axis.Configuration.Resources;

namespace Axis.Configuration.Modules;

/// <summary>
/// The manifest of a module version, <c>module.json</c>. <see cref="Modules"/> lists the modules it
/// depends on, with the entries of an application's <c>modules</c>.
/// </summary>
public sealed record ModuleManifest(string Id, string Version, string Label)
{
    /// <summary>The role slots of the module. Names are unique, ignoring letter case.</summary>
    public IReadOnlyList<ModuleRoleSlot> Roles { get; init; } = [];

    /// <summary>The connector slots of the module. Slot names are unique, and so are the operation names of a slot and the field names of an input or output, all ignoring letter case.</summary>
    public IReadOnlyList<ModuleConnectorSlot> Connectors { get; init; } = [];

    public IReadOnlyList<ApplicationModuleDefinition> Modules { get; init; } = [];
}
