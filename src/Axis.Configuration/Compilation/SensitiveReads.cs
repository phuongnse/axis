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
/// <c>findEntity</c> cannot find is skipped.
/// </summary>
internal static class SensitiveReads
{
    private static readonly HashSet<string> _aggregateNames =
        new(ExpressionFunctions.AggregateNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The entity name and field name of the first sensitive field that <paramref name="node"/>
    /// reads, or null when it reads none. <paramref name="entity"/> is the entity that a bare name
    /// belongs to, and <paramref name="scope"/> is the scope the expression was checked against.
    /// </summary>
    public static (string Entity, string Field)? Find(
        ExpressionNode node,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity) =>
        Walk(node, check, entity, scope, findEntity, inItem: false);

    // inItem is true inside the item expression of an aggregate, where a bare name is a field of the child row.
    private static (string Entity, string Field)? Walk(
        ExpressionNode node,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool inItem)
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

                return Walk(member.Target, check, entity, scope, findEntity, inItem);

            case UnaryNode unary:
                return Walk(unary.Operand, check, entity, scope, findEntity, inItem);

            case BinaryNode binary:
                return Walk(binary.Left, check, entity, scope, findEntity, inItem)
                    ?? Walk(binary.Right, check, entity, scope, findEntity, inItem);

            case IsNullNode isNull:
                return Walk(isNull.Operand, check, entity, scope, findEntity, inItem);

            case InNode inList:
                return Walk(inList.Operand, check, entity, scope, findEntity, inItem)
                    ?? FirstOf(inList.Items, check, entity, scope, findEntity, inItem);

            case CallNode call:
                return WalkCall(call, check, entity, scope, findEntity, inItem);

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
        bool inItem)
    {
        if (!check.RuleCalls.ContainsKey(call)
            && !inItem
            && _aggregateNames.Contains(call.Name)
            && call.Arguments is [NameNode list, ..]
            && scope.TryGetCollection(list.Name, out var collection)
            && findEntity(collection.Entity) is { } child)
        {
            return FirstOf(call.Arguments.Skip(1), check, child, scope, findEntity, inItem: true);
        }

        return FirstOf(call.Arguments, check, entity, scope, findEntity, inItem);
    }

    private static (string Entity, string Field)? FirstOf(
        IEnumerable<ExpressionNode> nodes,
        ExpressionCheckResult check,
        EntityResource entity,
        ExpressionScope scope,
        Func<string, EntityResource?> findEntity,
        bool inItem)
    {
        foreach (var node in nodes)
        {
            if (Walk(node, check, entity, scope, findEntity, inItem) is { } read)
            {
                return read;
            }
        }

        return null;
    }

    private static (string Entity, string Field)? Sensitive(EntityResource entity, string name)
    {
        var field = entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
        return field is { Sensitive: true } ? (entity.Name, field.Name) : null;
    }
}
