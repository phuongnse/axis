using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Builds the scope an entity's expressions are checked against: every field that has a column,
/// with its expression type. A child collection is left out, because only aggregates accept a list
/// and they are not built yet. A repeated name keeps its first field, ignoring letter case.
/// </summary>
public static class ExpressionScopes
{
    public static ExpressionScope ForEntity(IEnumerable<FieldModel> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Build(fields.Select(field => (field.Name, field.Type, field.Values, field.Target?.Name)));
    }

    internal static ExpressionScope ForEntity(IEnumerable<FieldDefinition> fields) =>
        Build(fields.Select(field => (field.Name, FieldTypes.Parse(field.Type), field.Values, field.Target)));

    private static ExpressionScope Build(IEnumerable<(string Name, FieldType Type, IReadOnlyList<string>? Values, string? Target)> fields)
    {
        var types = new Dictionary<string, ExpressionType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type, values, target) in fields)
        {
            if (TypeOf(name, type, values, target) is { } expressionType)
            {
                types.TryAdd(name, expressionType);
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
