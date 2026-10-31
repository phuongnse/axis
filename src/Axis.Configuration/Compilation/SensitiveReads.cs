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
/// expression of an aggregate reads the fields of the child entity. An entity that
/// <c>findEntity</c> cannot find is skipped. A policy grant's filter may test an exact lookup
/// field of its entity with <c>==</c> or <c>!=</c> against a literal other than <c>null</c>, outside
/// the arguments of a call and outside an item expression: that test runs against the stored
/// lookup hashes, so it is not a read.
/// </summary>
internal static class SensitiveReads
{
    private static readonly HashSet<string> _aggregateNames =
        new(ExpressionFunctions.AggregateNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The entity name and field name of the first sensitive field that <paramref name="node"/>
    /// reads, or null when it reads none. <paramref name="entity"/> is the entity that a bare name
    /// belongs to, and <paramref name="scope"/> is the scope the expression was checked against.
    /// <paramref name="allowExactLookupEquality"/> is true for a policy grant's filter.
    /// </summary>
    public static (string Entity, string Field)? Find(
        ExpressionNode node,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool allowExactLookupEquality = false) =>
        Walk(node, check, entity, scope, findEntity, inItem: false, inCall: false, allowExactLookupEquality);

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
        bool allowExactLookupEquality)
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

                return Walk(member.Target, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality);

            case UnaryNode unary:
                return Walk(unary.Operand, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality);

            case BinaryNode binary:
                if (allowExactLookupEquality && !inItem && !inCall && IsExactLookupEquality(binary, entity))
                {
                    return null;
                }

                return Walk(binary.Left, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality)
                    ?? Walk(binary.Right, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality);

            case IsNullNode isNull:
                return Walk(isNull.Operand, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality);

            case InNode inList:
                return Walk(inList.Operand, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality)
                    ?? FirstOf(inList.Items, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality);

            case CallNode call:
                return WalkCall(call, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality);

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
        bool allowExactLookupEquality)
    {
        if (!check.RuleCalls.ContainsKey(call)
            && !inItem
            && _aggregateNames.Contains(call.Name)
            && call.Arguments is [NameNode list, ..]
            && scope.TryGetCollection(list.Name, out var collection)
            && findEntity(collection.Entity) is { } child)
        {
            return FirstOf(call.Arguments.Skip(1), check, child, scope, findEntity, inItem: true, inCall: true, allowExactLookupEquality);
        }

        return FirstOf(call.Arguments, check, entity, scope, findEntity, inItem, inCall: true, allowExactLookupEquality);
    }

    private static (string Entity, string Field)? FirstOf(
        IEnumerable<ExpressionNode> nodes,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool inItem,
        bool inCall,
        bool allowExactLookupEquality)
    {
        foreach (var node in nodes)
        {
            if (Walk(node, check, entity, scope, findEntity, inItem, inCall, allowExactLookupEquality) is { } read)
            {
                return read;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="binary"/> is <c>==</c> or <c>!=</c> between a bare name that is an exact
    /// lookup field of <paramref name="entity"/>, in either order, and a text, integer, decimal or boolean literal.
    /// </summary>
    private static bool IsExactLookupEquality(BinaryNode binary, EntityResource entity) =>
        binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual
        && (IsLookupAgainstLiteral(binary.Left, binary.Right, entity) || IsLookupAgainstLiteral(binary.Right, binary.Left, entity));

    private static bool IsLookupAgainstLiteral(ExpressionNode field, ExpressionNode literal, EntityResource entity) =>
        field is NameNode name && IsLiteral(literal) && IsExactLookup(entity, name.Name);

    private static bool IsLiteral(ExpressionNode node) =>
        node is TextLiteral or IntegerLiteral or DecimalLiteral or BooleanLiteral;

    private static bool IsExactLookup(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
            is { Sensitive: true, ExactLookup: true };

    private static (string Entity, string Field)? Sensitive(EntityResource entity, string name)
    {
        var field = entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
        return field is { Sensitive: true } ? (entity.Name, field.Name) : null;
    }
}
