using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Builds the scope an entity's expressions are checked against: every field that has a column,
/// with its expression type. A computed field is left out when <c>includeComputed</c> is false,
/// as for the expression of a computed field. A repeated name keeps its first field, ignoring
/// letter case. When a child lookup is given, each child collection whose child entity it finds
/// is a collection an aggregate can name. Its item scope holds the child's fields, computed ones
/// included, with no rules and no collections. The named <c>rules</c> are callable only where they
/// are given, which for now is the top level of a validation, a data source filter and a process
/// condition. A data source scope adds the data source's parameters after the fields, has the named
/// rules it is given, and resolves paths through reference fields to the target entity's fields. A
/// process scope resolves paths the same way. No other scope resolves paths.
/// </summary>
public static class ExpressionScopes
{
    public static ExpressionScope ForEntity(
        IEnumerable<FieldModel> fields,
        bool includeComputed = true,
        IEnumerable<ExpressionRule>? rules = null,
        Func<FieldModel, EntityModel?>? childOf = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var all = fields.ToList();
        return Build(
            all
                .Where(field => includeComputed || !field.IsComputed)
                .Select(field => (field.Name, TypeOf(field))),
            rules,
            childOf is null
                ? null
                : all
                    .Where(field => field.Type == FieldType.ChildCollection)
                    .Select(field => childOf(field) is { } child
                        ? new ExpressionCollection(field.Name, child.Name, ForEntity(child.Fields))
                        : null)
                    .OfType<ExpressionCollection>()
                    .ToList(),
            referenceFields: null);
    }

    internal static ExpressionScope ForEntity(
        IEnumerable<FieldDefinition> fields,
        bool includeComputed = true,
        IEnumerable<ExpressionRule>? rules = null,
        Func<string, EntityResource?>? findEntity = null)
    {
        var all = fields.ToList();
        return Build(
            all
                .Where(field => includeComputed || field.Expression is null)
                .Select(field => (field.Name, TypeOf(field))),
            rules,
            findEntity is null ? null : CollectionsOf(all, findEntity),
            referenceFields: null);
    }

    /// <summary>
    /// The scope of a data source filter: the root entity's fields, then the parameters. A path
    /// through a reference field resolves to a field of the entity <paramref name="findEntity"/>
    /// returns for the target's name. The filter can call the named <paramref name="rules"/>.
    /// </summary>
    public static ExpressionScope ForDataSource(
        IEnumerable<FieldModel> fields,
        IEnumerable<DataSourceParameterModel> parameters,
        Func<string, EntityModel?> findEntity,
        IEnumerable<ExpressionRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(findEntity);
        return Build(
            [
                .. fields.Select(field => (field.Name, TypeOf(field))),
                .. parameters.Select(parameter => (parameter.Name, ParameterTypeOf(
                    parameter.Name, parameter.Type, parameter.Values, parameter.Target?.Name))),
            ],
            rules,
            collections: null,
            (target, name) => findEntity(target) is { } entity && entity.TryGetField(name, out var field) ? TypeOf(field) : null);
    }

    /// <summary>
    /// The scope of a data source filter: the root entity's fields, then the parameters. A path
    /// through a reference field resolves to a field of the entity <paramref name="findEntity"/>
    /// returns for the target's name. The child collections are in it too, so that the SQL
    /// translation reports an aggregate as outside the subset. The filter can call the named
    /// <paramref name="rules"/>.
    /// </summary>
    internal static ExpressionScope ForDataSource(
        IEnumerable<FieldDefinition> fields,
        IEnumerable<DataSourceParameterDefinition> parameters,
        Func<string, EntityResource?> findEntity,
        IEnumerable<ExpressionRule>? rules = null)
    {
        var all = fields.ToList();
        return Build(
            [
                .. all.Select(field => (field.Name, TypeOf(field))),
                .. parameters.Select(parameter => (parameter.Name, ParameterTypeOf(
                    parameter.Name, FieldTypes.Parse(parameter.Type), parameter.Values, parameter.Target))),
            ],
            rules,
            CollectionsOf(all, findEntity),
            ReferenceFieldsOf(findEntity));
    }

    /// <summary>
    /// The scope of a process condition: every field of the subject entity, computed ones
    /// included, its child collections, which only aggregates accept, and the named
    /// <paramref name="rules"/>. A path through a reference field resolves to a field of the entity
    /// <paramref name="findEntity"/> returns for the target's name.
    /// </summary>
    internal static ExpressionScope ForProcess(
        IEnumerable<FieldDefinition> fields,
        IEnumerable<ExpressionRule> rules,
        Func<string, EntityResource?> findEntity)
    {
        var all = fields.ToList();
        return Build(
            all.Select(field => (field.Name, TypeOf(field))),
            rules,
            CollectionsOf(all, findEntity),
            ReferenceFieldsOf(findEntity));
    }

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

    /// <summary>Each child collection whose child entity is found, with the child's fields, computed ones included, as its item scope.</summary>
    private static List<ExpressionCollection> CollectionsOf(
        IEnumerable<FieldDefinition> fields, Func<string, EntityResource?> findEntity) =>
        [
            .. fields
                .Where(field => FieldTypes.Parse(field.Type) == FieldType.ChildCollection && field.Target is not null)
                .Select(field => findEntity(field.Target!) is { } child
                    ? new ExpressionCollection(field.Name, child.Name, ForEntity(child.Fields))
                    : null)
                .OfType<ExpressionCollection>(),
        ];

    /// <summary>Finds a field of the entity <paramref name="findEntity"/> returns for a reference's target, ignoring letter case.</summary>
    private static ReferenceFieldResolver ReferenceFieldsOf(Func<string, EntityResource?> findEntity) =>
        (target, name) => findEntity(target)?.Fields
            .FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)) is { } field
            ? TypeOf(field)
            : null;

    private static ExpressionScope Build(
        IEnumerable<(string Name, ExpressionType? Type)> fields,
        IEnumerable<ExpressionRule>? rules,
        IEnumerable<ExpressionCollection>? collections,
        ReferenceFieldResolver? referenceFields)
    {
        var types = new Dictionary<string, ExpressionType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in fields)
        {
            if (type is not null)
            {
                types.TryAdd(name, type);
            }
        }

        return new ExpressionScope(types, rules, collections, referenceFields);
    }

    /// <summary>
    /// A parameter's type is a field's, except that an enum parameter declares its own values and a
    /// reference parameter is marked as one, so a path cannot start at it.
    /// </summary>
    private static ExpressionType? ParameterTypeOf(string name, FieldType type, IReadOnlyList<string>? values, string? target) =>
        type switch
        {
            FieldType.Enum => ExpressionType.EnumParameter(name, values ?? []),
            FieldType.Reference => ExpressionType.ReferenceParameter(target ?? ""),
            _ => TypeOf(name, type, values, target),
        };

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
