using System.Collections.Frozen;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>How a customization layer finds an element of a list.</summary>
internal enum ListKeyMode
{
    /// <summary>The elements have a key and a layer changes one by its key.</summary>
    Keyed,

    /// <summary>A layer replaces the elements as a whole.</summary>
    Value,
}

/// <summary>
/// The key of a list. <see cref="Key"/> is set only for a keyed list. <see cref="Ordered"/> is set for a keyed
/// list where order counts, so a layer can place an element in it with <c>after</c> or <c>before</c>.
/// </summary>
internal readonly record struct ListKey(ListKeyMode Mode, string? Key, bool Ordered = false);

/// <summary>
/// The key of every object list that a customization layer can change, by resource kind and the name of the
/// property that holds the list. A test keeps it in step with the resource schemas.
/// </summary>
internal static class ListKeys
{
    private static readonly ListKey _name = new(ListKeyMode.Keyed, "name");
    private static readonly ListKey _field = new(ListKeyMode.Keyed, "field");
    private static readonly ListKey _orderedName = new(ListKeyMode.Keyed, "name", true);
    private static readonly ListKey _orderedField = new(ListKeyMode.Keyed, "field", true);
    private static readonly ListKey _id = new(ListKeyMode.Keyed, "id");
    private static readonly ListKey _value = new(ListKeyMode.Value, null);

    private static readonly FrozenDictionary<(string Kind, string Property), ListKey> _keys =
        new Dictionary<(string Kind, string Property), ListKey>
        {
            [(ResourceKinds.Entity, "fields")] = _orderedName,
            [(ResourceKinds.Entity, "validations")] = _name,
            [(ResourceKinds.Entity, "indexes")] = _name,
            [(ResourceKinds.Process, "steps")] = _orderedName,
            [(ResourceKinds.Process, "outcomes")] = _name,
            [(ResourceKinds.Process, "branches")] = _name,
            [(ResourceKinds.Process, "escalations")] = _name,
            [(ResourceKinds.Process, "parameters")] = _value,
            [(ResourceKinds.Process, "to")] = _value,
            [(ResourceKinds.Policy, "grants")] = _name,
            [(ResourceKinds.Page, "widgets")] = _orderedName,
            [(ResourceKinds.Page, "actions")] = _orderedName,
            [(ResourceKinds.Page, "rowActions")] = _orderedName,
            [(ResourceKinds.Page, "toolbarActions")] = _orderedName,
            [(ResourceKinds.Page, "columns")] = _orderedName,
            [(ResourceKinds.Page, "tabs")] = _orderedName,
            [(ResourceKinds.Page, "parameters")] = _value,
            [(ResourceKinds.Page, "measures")] = _value,
            [(ResourceKinds.Form, "sections")] = _orderedName,
            [(ResourceKinds.Form, "steps")] = _orderedName,
            [(ResourceKinds.Form, "fields")] = _orderedField,
            [(ResourceKinds.DataSource, "fields")] = _name,
            [(ResourceKinds.DataSource, "parameters")] = _name,
            [(ResourceKinds.DataSource, "groupBy")] = _field,
            [(ResourceKinds.DataSource, "measures")] = _value,
            [(ResourceKinds.Seed, "records")] = _id,
            [(ResourceKinds.Contract, "states")] = _name,
            [(ResourceKinds.Contract, "transitions")] = _name,
            [(ResourceKinds.Contract, "validations")] = _value,
            [(ResourceKinds.Contract, "effects")] = _value,
            [(ResourceKinds.Contract, "others")] = _value,
            [(ResourceKinds.Contract, "slots")] = _value,
            [(ResourceKinds.Connector, "operations")] = _value,
            [(ResourceKinds.Connector, "input")] = _value,
            [(ResourceKinds.Connector, "output")] = _value,
            [(ResourceKinds.Connector, "fields")] = _value,
            [(ResourceKinds.Site, "navigation")] = _value,
            [(ResourceKinds.Endpoint, "input")] = _value,
            [(ResourceKinds.Migration, "changes")] = _value,
            [(ResourceKinds.Rule, "parameters")] = _value,
            [(ResourceKinds.Event, "fields")] = _value,
        }.ToFrozenDictionary();

    /// <summary>Every list in the table, by resource kind and property name.</summary>
    internal static IEnumerable<KeyValuePair<(string Kind, string Property), ListKey>> Entries => _keys;

    /// <summary>Finds the key of the object list that the property <paramref name="property"/> of a <paramref name="kind"/> holds.</summary>
    public static bool TryGet(string kind, string property, out ListKey key) =>
        _keys.TryGetValue((kind, property), out key);
}
