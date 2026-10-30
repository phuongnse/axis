using Axis.Configuration.Resources;

namespace Axis.Configuration.Modules;

/// <summary>
/// A connector slot a module declares in <c>module.json</c>: the outside operations the module
/// needs, with a label. Each application maps every slot to one of its own connectors.
/// </summary>
public sealed record ModuleConnectorSlot(string Name, TextReference Label, IReadOnlyList<ModuleConnectorSlotOperation> Operations);

/// <summary>
/// An operation of a connector slot: a name and the typed fields it sends and reads. The fields have
/// the shape of a connector operation's fields.
/// </summary>
public sealed record ModuleConnectorSlotOperation(string Name)
{
    /// <summary>The fields the module sends.</summary>
    public IReadOnlyList<ConnectorFieldDefinition> Input { get; init; } = [];

    /// <summary>The fields the module reads from the response.</summary>
    public IReadOnlyList<ConnectorFieldDefinition> Output { get; init; } = [];
}
