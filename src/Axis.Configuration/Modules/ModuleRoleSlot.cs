using Axis.Configuration.Resources;

namespace Axis.Configuration.Modules;

/// <summary>
/// A role slot a module declares in <c>module.json</c>: a name the module's policies and tasks can
/// use, and a label. Each application maps every slot to one of its own roles.
/// </summary>
public sealed record ModuleRoleSlot(string Name, TextReference Label);
