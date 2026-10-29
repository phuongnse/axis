namespace Axis.Configuration.Modules;

/// <summary>The manifest of a module version, <c>module.json</c>.</summary>
public sealed record ModuleManifest(string Id, string Version, string Label)
{
    /// <summary>The role slots of the module. Names are unique, ignoring letter case.</summary>
    public IReadOnlyList<ModuleRoleSlot> Roles { get; init; } = [];
}
