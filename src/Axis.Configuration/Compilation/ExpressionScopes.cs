using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Builds the scope an entity's expressions are checked against: every field that has a column,
/// with its expression type. A child collection is left out, because only aggregates accept a list
/// and they are not built yet. A computed field is left out when <c>includeComputed</c> is false,
/// as for the expression of a computed field. A repeated name keeps its first field, ignoring
/// letter case.
/// </summary>
public static class ExpressionScopes
{
    public static ExpressionScope ForEntity(IEnumerable<FieldModel> fields, bool includeComputed = true)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Build(fields
            .Where(field => includeComputed || !field.IsComputed)
            .Select(field => (field.Name, TypeOf(field))));
    }

    internal static ExpressionScope ForEntity(IEnumerable<FieldDefinition> fields, bool includeComputed = true) =>
        Build(fields
            .Where(field => includeComputed || field.Expression is null)
            .Select(field => (field.Name, TypeOf(field))));

    /// <summary>The expression type of a field's value, or null for a child collection.</summary>
    public static ExpressionType? TypeOf(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return TypeOf(field.Name, field.Type, field.Values, field.Target?.Name);
    }

    /// <summary>The expression type of a field's value, or null for a child collection.</summary>
    internal static ExpressionType? TypeOf(FieldDefinition field) =>
        TypeOf(field.Name, FieldTypes.Parse(field.Type), field.Values, field.Target);

    private static ExpressionScope Build(IEnumerable<(string Name, ExpressionType? Type)> fields)
    {
        var types = new Dictionary<string, ExpressionType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in fields)
        {
            if (type is not null)
            {
                types.TryAdd(name, type);
            }
        }

        return new ExpressionScope(types);
    }

    private static ExpressionType? TypeOf(string name, FieldType type, IReadOnlyList<string>? values, string? target) =>
        type switch
        {
            FieldType.Text => ExpressionType.Text,
            FieldType.Integer => ExpressionType.Integer,
            FieldType.Decimal => ExpressionType.Decimal,
            FieldType.Boolean => ExpressionType.Boolean,
            FieldType.Date => ExpressionType.Date,
            FieldType.DateTime => ExpressionType.DateTime,
            FieldType.Enum => ExpressionType.Enum(name, values ?? []),
            FieldType.Reference => ExpressionType.Reference(target ?? ""),
            FieldType.ChildCollection => null,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown field type."),
        };
}
