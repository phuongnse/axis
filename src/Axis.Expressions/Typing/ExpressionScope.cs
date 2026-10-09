using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Typing;

/// <summary>
/// Finds the type of the field <paramref name="fieldName"/> of the entity
/// <paramref name="targetEntity"/>, or null when the entity has no such field with a value.
/// </summary>
public delegate ExpressionType? ReferenceFieldResolver(string targetEntity, string fieldName);

/// <summary>
/// The fields an expression may name, with their types, and the named rules it may call. Names
/// match ignoring letter case. A repeated rule name keeps its first rule. A scope with a
/// <see cref="ReferenceFieldResolver"/> also resolves paths through reference fields, such as
/// <c>department.name</c>. A scope without one reports every path as an unknown name.
/// </summary>
public sealed class ExpressionScope
{
    private readonly Dictionary<string, ExpressionType> _fields;
    private readonly Dictionary<string, ExpressionRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReferenceFieldResolver? _referenceFields;

    public ExpressionScope(
        IEnumerable<KeyValuePair<string, ExpressionType>> fields,
        IEnumerable<ExpressionRule>? rules = null,
        ReferenceFieldResolver? referenceFields = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<string, ExpressionType>(fields, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules ?? [])
        {
            _rules.TryAdd(rule.Name, rule);
        }

        _referenceFields = referenceFields;
    }

    /// <summary>Whether paths through reference fields resolve in this scope.</summary>
    public bool ResolvesPaths => _referenceFields is not null;

    public bool TryGetField(string name, [NotNullWhen(true)] out ExpressionType? type) =>
        _fields.TryGetValue(name, out type);

    public bool TryGetRule(string name, [NotNullWhen(true)] out ExpressionRule? rule) =>
        _rules.TryGetValue(name, out rule);

    /// <summary>
    /// Finds the field <paramref name="name"/> of the entity that <paramref name="reference"/>
    /// points to. False without a resolver, when <paramref name="reference"/> is not a reference,
    /// or when the target has no such field with a value.
    /// </summary>
    public bool TryGetReferenceField(ExpressionType reference, string name, [NotNullWhen(true)] out ExpressionType? type)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(name);
        type = _referenceFields is not null && reference.Kind == ExpressionTypeKind.Reference
            ? _referenceFields(reference.Source ?? "", name)
            : null;
        return type is not null;
    }
}
