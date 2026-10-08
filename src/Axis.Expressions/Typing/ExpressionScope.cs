using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Typing;

/// <summary>The fields an expression may name, with their types. Names match ignoring letter case.</summary>
public sealed class ExpressionScope
{
    private readonly Dictionary<string, ExpressionType> _fields;

    public ExpressionScope(IEnumerable<KeyValuePair<string, ExpressionType>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<string, ExpressionType>(fields, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetField(string name, [NotNullWhen(true)] out ExpressionType? type) =>
        _fields.TryGetValue(name, out type);
}
