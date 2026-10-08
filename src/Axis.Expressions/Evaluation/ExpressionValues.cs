namespace Axis.Expressions.Evaluation;

/// <summary>
/// The field values an expression is evaluated against. Names match ignoring letter case. Each
/// value is <c>null</c> or has the CLR type of its field: <see cref="string"/> for text and enum,
/// <see cref="long"/> for integer, <see cref="decimal"/>, <see cref="bool"/>, <see cref="DateOnly"/>
/// for date, <see cref="DateTimeOffset"/> in UTC for date-time, and <see cref="Guid"/> for reference.
/// </summary>
public sealed class ExpressionValues
{
    private readonly Dictionary<string, object?> _values;

    public ExpressionValues(IEnumerable<KeyValuePair<string, object?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetValue(string name, out object? value) => _values.TryGetValue(name, out value);
}
