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

    /// <summary>The list will be keyed, but its schema does not require the key yet.</summary>
    Pending,
}

/// <summary>The key of a list. <see cref="Key"/> is set only for a keyed list.</summary>
internal readonly record struct ListKey(ListKeyMode Mode, string? Key);

/// <summary>
/// The key of every object list that a customization layer can change, by resource kind and the name of the
/// property that holds the list. A test keeps it in step with the resource schemas.
/// </summary>
internal static class ListKeys
{
    private static readonly ListKey _name = new(ListKeyMode.Keyed, "name");
    private static readonly ListKey _field = new(ListKeyMode.Keyed, "field");
    private static readonly ListKey _id = new(ListKeyMode.Keyed, "id");
    private static readonly ListKey _value = new(ListKeyMode.Value, null);
    private static readonly ListKey _pending = new(ListKeyMode.Pending, null);

    private static readonly FrozenDictionary<(string Kind, string Property), ListKey> _keys =
        new Dictionary<(string Kind, string Property), ListKey>
        {
            [(ResourceKinds.Entity, "fields")] = _name,
            [(ResourceKinds.Entity, "validations")] = _name,
            [(ResourceKinds.Entity, "indexes")] = _name,
            [(ResourceKinds.Process, "steps")] = _name,
            [(ResourceKinds.Process, "outcomes")] = _name,
            [(ResourceKinds.Process, "branches")] = _pending,
            [(ResourceKinds.Process, "escalations")] = _pending,
            [(ResourceKinds.Process, "parameters")] = _value,
            [(ResourceKinds.Process, "to")] = _value,
            [(ResourceKinds.Policy, "grants")] = _name,
            [(ResourceKinds.Page, "widgets")] = _pending,
            [(ResourceKinds.Page, "actions")] = _pending,
            [(ResourceKinds.Page, "rowActions")] = _pending,
            [(ResourceKinds.Page, "toolbarActions")] = _pending,
            [(ResourceKinds.Page, "columns")] = _pending,
            [(ResourceKinds.Page, "tabs")] = _pending,
            [(ResourceKinds.Page, "parameters")] = _value,
            [(ResourceKinds.Page, "measures")] = _value,
            [(ResourceKinds.Form, "sections")] = _pending,
            [(ResourceKinds.Form, "steps")] = _pending,
            [(ResourceKinds.Form, "fields")] = _field,
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
