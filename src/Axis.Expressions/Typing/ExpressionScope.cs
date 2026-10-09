using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Typing;

/// <summary>
/// A child collection an aggregate can name: the collection field, the child entity's name and the
/// scope of the aggregate's item expression, which holds the child row's fields.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "A child collection is the concept's name.")]
public sealed record ExpressionCollection(string Name, string Entity, ExpressionScope Items);

/// <summary>
/// The fields an expression may name, with their types, the named rules it may call and the child
/// collections its aggregates may name. Names match ignoring letter case. A repeated rule or
/// collection name keeps its first one.
/// </summary>
public sealed class ExpressionScope
{
    private readonly Dictionary<string, ExpressionType> _fields;
    private readonly Dictionary<string, ExpressionRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExpressionCollection> _collections = new(StringComparer.OrdinalIgnoreCase);

    public ExpressionScope(
        IEnumerable<KeyValuePair<string, ExpressionType>> fields,
        IEnumerable<ExpressionRule>? rules = null,
        IEnumerable<ExpressionCollection>? collections = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<string, ExpressionType>(fields, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules ?? [])
        {
            _rules.TryAdd(rule.Name, rule);
        }

        foreach (var collection in collections ?? [])
        {
            _collections.TryAdd(collection.Name, collection);
        }
    }

    public bool TryGetField(string name, [NotNullWhen(true)] out ExpressionType? type) =>
        _fields.TryGetValue(name, out type);

    public bool TryGetRule(string name, [NotNullWhen(true)] out ExpressionRule? rule) =>
        _rules.TryGetValue(name, out rule);

    public bool TryGetCollection(string name, [NotNullWhen(true)] out ExpressionCollection? collection) =>
        _collections.TryGetValue(name, out collection);
}
