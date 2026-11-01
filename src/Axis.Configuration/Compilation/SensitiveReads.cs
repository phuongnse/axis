using Axis.Configuration.Resources;
using Axis.Expressions.Functions;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Finds a field marked <c>sensitive</c> that a checked expression reads. It reads the same fields
/// as <see cref="Model.MappingFields"/> does for a mapping: a bare name is a field of the current
/// entity, a path step is a field of the entity the step's target resolves to, a rule call reads
/// the fields its arguments read, because a rule body sees only its parameters, and the item
/// expression of an aggregate reads the fields of the child entity, also when the aggregate names the
/// collection by a path, such as <c>type.steps</c>, where the path's reference part reads its own fields. An entity that
/// <c>findEntity</c> cannot find is skipped. A filter may test an exact lookup
/// field of its entity with <c>==</c> or <c>!=</c> against a literal other than <c>null</c>, outside
/// the arguments of a call and outside an item expression: that test runs against the stored
/// lookup hashes, so it is not a read. A data source's filter and a measure's filter may also test
/// it against a text, enum, integer, decimal or boolean parameter of the data source.
/// </summary>
internal static class SensitiveReads
{
    private static readonly HashSet<string> _aggregateNames =
        new(ExpressionFunctions.AggregateNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The entity name and field name of the first sensitive field that <paramref name="node"/>
    /// reads, or null when it reads none. <paramref name="entity"/> is the entity that a bare name
    /// belongs to, and <paramref name="scope"/> is the scope the expression was checked against.
    /// <paramref name="allowExactLookupEquality"/> is true for a filter, which may test an exact lookup
    /// field. <paramref name="isParameter"/> says whether a name is a data source parameter, which a
    /// data source filter may test the field against. Without it, only a literal is allowed.
    /// </summary>
    public static (string Entity, string Field)? Find(
        ExpressionNode node,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool allowExactLookupEquality = false,
        Func<string, bool>? isParameter = null) =>
        Walk(node, check, entity, scope, findEntity, inItem: false, inCall: false, allowExactLookupEquality, isParameter);

    // inItem is true inside the item expression of an aggregate, where a bare name is a field of the child row.
    // inCall is true inside the arguments of a call, where an exact lookup test is not translated.
    private static (string Entity, string Field)? Walk(
        ExpressionNode node,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool inItem,
        bool inCall,
        bool allowExactLookupEquality,
        Func<string, bool>? isParameter)
    {
        switch (node)
        {
            case NameNode name:
                return Sensitive(entity, name.Name);

            case MemberNode member:
                if (check.PathTargets.TryGetValue(member, out var targetName)
                    && findEntity(targetName) is { } target
                    && Sensitive(target, member.Name) is { } read)
                {
                    return read;
                }

                return Walk(member.Target, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter);

            case UnaryNode unary:
                return Walk(unary.Operand, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter);

            case BinaryNode binary:
                if (allowExactLookupEquality && !inItem && !inCall && IsExactLookupEquality(binary, entity, scope, isParameter))
                {
                    return null;
                }

                return Walk(binary.Left, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter)
                    ?? Walk(binary.Right, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter);

            case IsNullNode isNull:
                return Walk(isNull.Operand, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter);

            case InNode inList:
                return Walk(inList.Operand, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter)
                    ?? FirstOf(inList.Items, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter);

            case CallNode call:
                return WalkCall(call, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter);

            default:
                return null;
        }
    }

    private static (string Entity, string Field)? WalkCall(
        CallNode call,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool inItem,
        bool inCall,
        bool allowExactLookupEquality,
        Func<string, bool>? isParameter)
    {
        if (!check.RuleCalls.ContainsKey(call)
            && !inItem
            && _aggregateNames.Contains(call.Name)
            && call.Arguments is [NameNode list, ..]
            && scope.TryGetCollection(list.Name, out var collection)
            && findEntity(collection.Entity) is { } child)
        {
            return FirstOf(call.Arguments.Skip(1), check, child, scope, findEntity, inItem: true, inCall: true, allowExactLookupEquality, isParameter);
        }

        if (!check.RuleCalls.ContainsKey(call)
            && !inItem
            && _aggregateNames.Contains(call.Name)
            && call.Arguments is [MemberNode path, ..]
            && check.CollectionPaths.Contains(path)
            && check.PathTargets.TryGetValue(path, out var ownerName)
            && findEntity(ownerName) is { } owner
            && owner.Fields.FirstOrDefault(field => string.Equals(field.Name, path.Name, StringComparison.OrdinalIgnoreCase)) is { Target: { } childName }
            && findEntity(childName) is { } pathChild)
        {
            // The path's reference part is read from the current entity, and the item expression from the collection's rows.
            return Walk(path.Target, check, entity, scope, findEntity, inItem, inCall: true, allowExactLookupEquality, isParameter)
                ?? FirstOf(call.Arguments.Skip(1), check, pathChild, scope, findEntity, inItem: true, inCall: true, allowExactLookupEquality, isParameter);
        }

        return FirstOf(call.Arguments, check, entity, scope, findEntity, inItem, inCall: true, allowExactLookupEquality, isParameter);
    }

    private static (string Entity, string Field)? FirstOf(
        IEnumerable<ExpressionNode> nodes,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool inItem,
        bool inCall,
        bool allowExactLookupEquality,
        Func<string, bool>? isParameter)
    {
        foreach (var node in nodes)
        {
            if (Walk(node, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality, isParameter) is { } read)
            {
                return read;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="binary"/> is <c>==</c> or <c>!=</c> between a bare name that is an exact
    /// lookup field of <paramref name="entity"/>, in either order, and a text, integer, decimal or boolean literal
    /// or a data source parameter of one of those kinds or an enum.
    /// </summary>
    private static bool IsExactLookupEquality(
        BinaryNode binary, EntityResource entity, ExpressionScope scope, Func<string, bool>? isParameter) =>
        binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
        && (IsLookupAgainstValue(binary.Left, binary.Right, entity, scope, isParameter)
            || IsLookupAgainstValue(binary.Right, binary.Left, entity, scope, isParameter));

    private static bool IsLookupAgainstValue(
        ExpressionNode field, ExpressionNode value, EntityResource entity, ExpressionScope scope, Func<string, bool>? isParameter) =>
        field is NameNode name && (IsLiteral(value) || IsParameter(value, scope, isParameter)) && IsExactLookup(entity, name.Name);

    private static bool IsLiteral(ExpressionNode node) =>
        node is TextLiteral or IntegerLiteral or DecimalLiteral or BooleanLiteral;

    private static bool IsParameter(ExpressionNode node, ExpressionScope scope, Func<string, bool>? isParameter) =>
        isParameter is not null
        && node is NameNode name
        && isParameter(name.Name)
        && scope.TryGetField(name.Name, out var type)
        && type.Kind is ExpressionTypeKind.Text or ExpressionTypeKind.Enum or ExpressionTypeKind.Integer
            or ExpressionTypeKind.Decimal or ExpressionTypeKind.Boolean;

    private static bool IsExactLookup(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
            is { Sensitive: true, ExactLookup: true };

    private static (string Entity, string Field)? Sensitive(EntityResource entity, string name)
    {
        var field = entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
        return field is { Sensitive: true } ? (entity.Name, field.Name) : null;
    }
}
