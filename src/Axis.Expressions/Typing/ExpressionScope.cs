using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Typing;

/// <summary>
/// The fields an expression may name, with their types, and the named rules it may call. Names
/// match ignoring letter case. A repeated rule name keeps its first rule.
/// </summary>
public sealed class ExpressionScope
{
    private readonly Dictionary<string, ExpressionType> _fields;
    private readonly Dictionary<string, ExpressionRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public ExpressionScope(IEnumerable<KeyValuePair<string, ExpressionType>> fields, IEnumerable<ExpressionRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<string, ExpressionType>(fields, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules ?? [])
        {
            _rules.TryAdd(rule.Name, rule);
        }
    }

    public bool TryGetField(string name, [NotNullWhen(true)] out ExpressionType? type) =>
        _fields.TryGetValue(name, out type);

    public bool TryGetRule(string name, [NotNullWhen(true)] out ExpressionRule? rule) =>
        _rules.TryGetValue(name, out rule);
}
