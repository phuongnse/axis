using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Typing;

/// <summary>
/// The type of an expression or field. An enum type belongs to the field that declares its values,
/// and a reference type to its target entity. Two types are equal when their kind matches and their
/// field or entity matches ignoring letter case. The enum values are not part of equality.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The names are the expression types of the language.")]
public sealed class ExpressionType : IEquatable<ExpressionType>
{
    private ExpressionType(ExpressionTypeKind kind, string? source, IReadOnlyList<string> values, bool isParameter = false)
    {
        Kind = kind;
        Source = source;
        Values = values;
        IsParameter = isParameter;
    }

    public static ExpressionType Null { get; } = new(ExpressionTypeKind.Null, null, []);

    public static ExpressionType Text { get; } = new(ExpressionTypeKind.Text, null, []);

    public static ExpressionType Integer { get; } = new(ExpressionTypeKind.Integer, null, []);

    public static ExpressionType Decimal { get; } = new(ExpressionTypeKind.Decimal, null, []);

    public static ExpressionType Boolean { get; } = new(ExpressionTypeKind.Boolean, null, []);

    public static ExpressionType Date { get; } = new(ExpressionTypeKind.Date, null, []);

    public static ExpressionType DateTime { get; } = new(ExpressionTypeKind.DateTime, null, []);

    public ExpressionTypeKind Kind { get; }

    /// <summary>For an enum, the field that declares the values. For a reference, the target entity.</summary>
    public string? Source { get; }

    /// <summary>The values of an enum, compared ordinally. Empty for other kinds.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Whether this enum belongs to a data source parameter. Such an enum compares with another
    /// enum when all of its values are in the other's values.
    /// </summary>
    public bool IsParameter { get; }

    public static ExpressionType Enum(string field, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(values);
        return new ExpressionType(ExpressionTypeKind.Enum, field, [.. values]);
    }

    /// <summary>The enum of a data source parameter, which declares its own values.</summary>
    public static ExpressionType EnumParameter(string parameter, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(values);
        return new ExpressionType(ExpressionTypeKind.Enum, parameter, [.. values], isParameter: true);
    }

    public static ExpressionType Reference(string targetEntity)
    {
        ArgumentNullException.ThrowIfNull(targetEntity);
        return new ExpressionType(ExpressionTypeKind.Reference, targetEntity, []);
    }

    public bool Equals(ExpressionType? other) =>
        other is not null && Kind == other.Kind && string.Equals(Source, other.Source, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ExpressionType);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Source is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Source));

    public static bool operator ==(ExpressionType? left, ExpressionType? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ExpressionType? left, ExpressionType? right) => !(left == right);

    /// <summary>The type as messages name it, such as <c>date-time</c> or <c>enum of 'status'</c>.</summary>
    public override string ToString() => Kind switch
    {
        ExpressionTypeKind.Null => "null",
        ExpressionTypeKind.Text => "text",
        ExpressionTypeKind.Integer => "integer",
        ExpressionTypeKind.Decimal => "decimal",
        ExpressionTypeKind.Boolean => "boolean",
        ExpressionTypeKind.Date => "date",
        ExpressionTypeKind.DateTime => "date-time",
        ExpressionTypeKind.Enum => $"enum of '{Source}'",
        ExpressionTypeKind.Reference => $"reference to '{Source}'",
        _ => Kind.ToString(),
    };
}
