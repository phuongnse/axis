using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Builds the scope an entity's expressions are checked against: every field that has a column,
/// with its expression type. A child collection is left out, because only aggregates accept a list
/// and they are not built yet. A computed field is left out when <c>includeComputed</c> is false,
/// as for the expression of a computed field. A repeated name keeps its first field, ignoring
/// letter case. The named <c>rules</c> are callable only where they are given, which for now is
/// validations. A data source scope adds the data source's parameters after the fields, and has no
/// rules.
/// </summary>
public static class ExpressionScopes
{
    public static ExpressionScope ForEntity(
        IEnumerable<FieldModel> fields, bool includeComputed = true, IEnumerable<ExpressionRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Build(
            fields
                .Where(field => includeComputed || !field.IsComputed)
                .Select(field => (field.Name, TypeOf(field))),
            rules);
    }

    internal static ExpressionScope ForEntity(
        IEnumerable<FieldDefinition> fields, bool includeComputed = true, IEnumerable<ExpressionRule>? rules = null) =>
        Build(
            fields
                .Where(field => includeComputed || field.Expression is null)
                .Select(field => (field.Name, TypeOf(field))),
            rules);

    /// <summary>The scope of a data source filter: the root entity's fields, then the parameters.</summary>
    public static ExpressionScope ForDataSource(IEnumerable<FieldModel> fields, IEnumerable<DataSourceParameterModel> parameters)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(parameters);
        return Build(
            [
                .. fields.Select(field => (field.Name, TypeOf(field))),
                .. parameters.Select(parameter => (parameter.Name, ParameterTypeOf(
                    parameter.Name, parameter.Type, parameter.Values, parameter.Target?.Name))),
            ],
            rules: null);
    }

    /// <summary>The scope of a data source filter: the root entity's fields, then the parameters.</summary>
    internal static ExpressionScope ForDataSource(
        IEnumerable<FieldDefinition> fields, IEnumerable<DataSourceParameterDefinition> parameters) =>
        Build(
            [
                .. fields.Select(field => (field.Name, TypeOf(field))),
                .. parameters.Select(parameter => (parameter.Name, ParameterTypeOf(
                    parameter.Name, FieldTypes.Parse(parameter.Type), parameter.Values, parameter.Target))),
            ],
            rules: null);

    /// <summary>The expression type of a field's value, or null for a child collection.</summary>
    public static ExpressionType? TypeOf(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return TypeOf(field.Name, field.Type, field.Values, field.Target?.Name);
    }

    /// <summary>The expression type of a field's value, or null for a child collection.</summary>
    internal static ExpressionType? TypeOf(FieldDefinition field) =>
        TypeOf(field.Name, FieldTypes.Parse(field.Type), field.Values, field.Target);

    /// <summary>The expression type of a scalar type name other than <c>enum</c>, as a rule parameter or result declares it.</summary>
    internal static ExpressionType TypeOf(string scalarType) => TypeOf("", FieldTypes.Parse(scalarType), null, null)!;

    private static ExpressionScope Build(IEnumerable<(string Name, ExpressionType? Type)> fields, IEnumerable<ExpressionRule>? rules)
    {
        var types = new Dictionary<string, ExpressionType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in fields)
        {
            if (type is not null)
            {
                types.TryAdd(name, type);
            }
        }

        return new ExpressionScope(types, rules);
    }

    /// <summary>A parameter's type is a field's, except that an enum parameter declares its own values.</summary>
    private static ExpressionType? ParameterTypeOf(string name, FieldType type, IReadOnlyList<string>? values, string? target) =>
        type == FieldType.Enum ? ExpressionType.EnumParameter(name, values ?? []) : TypeOf(name, type, values, target);

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
