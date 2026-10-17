using Axis.Expressions.Functions;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Model;

/// <summary>
/// A field of an entity, by the entity's id and the field's declared name. Fields have no ids,
/// and entities keep theirs across releases.
/// </summary>
public sealed record MappedField(Guid EntityId, string Field);

/// <summary>
/// Collects the fields a connector or package call's input and output mapping reads or writes, so
/// the instance detail can show a call's stored input only to a user who may read all of them. The
/// walker fails closed: it returns null, which means every value is hidden, for anything it does
/// not understand.
/// </summary>
public static class MappingFields
{
    private static readonly HashSet<string> _aggregateNames =
        new(ExpressionFunctions.AggregateNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The fields that <paramref name="input"/> reads and <paramref name="output"/> writes on the
    /// <paramref name="subject"/> entity of the <paramref name="pinned"/> release, on any entity:
    /// <list type="bullet">
    /// <item>every field on a reference path, such as <c>department</c> and the <c>name</c> of the department entity for <c>department.name</c>,</item>
    /// <item>every field passed as an argument to a rule, which is where a rule reads its fields from, because its body sees only its parameters,</item>
    /// <item>the owner's child-collection field for an aggregate such as <c>sum(lines, amount)</c>, because child rows are read through the owner, and the target fields of any path in its item expression,</item>
    /// <item>the subject field each output is written to.</item>
    /// </list>
    /// Returns null when the mapping uses a name, path, call or node type the walker does not
    /// resolve, so the caller hides the values.
    /// </summary>
    public static IReadOnlySet<MappedField>? Collect(
        ApplicationModel pinned,
        EntityModel subject,
        IEnumerable<ConnectorInputModel> input,
        IEnumerable<ConnectorOutputModel> output)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var fields = new HashSet<MappedField>();
        foreach (var item in input)
        {
            if (!Walk(pinned, item.Value.Syntax, item.Value.Check, subject, inItem: false, fields))
            {
                return null;
            }
        }

        foreach (var item in output)
        {
            if (!Add(subject, item.Field, fields))
            {
                return null;
            }
        }

        return fields;
    }

    // inItem is true inside the item expression of an aggregate, where a bare name is a field of the child row.
    private static bool Walk(ApplicationModel pinned, ExpressionNode node, ExpressionCheckResult check, EntityModel entity, bool inItem, HashSet<MappedField> fields)
    {
        switch (node)
        {
            case IntegerLiteral or DecimalLiteral or TextLiteral or BooleanLiteral or NullLiteral:
                return true;

            case NameNode name:
                // A child row's field counts through the owner's collection field.
                return inItem ? entity.TryGetField(name.Name, out _) : Add(entity, name.Name, fields);

            case MemberNode member:
                return check.PathTargets.TryGetValue(member, out var targetName)
                    && pinned.TryGetEntity(targetName, out var target)
                    && Add(target, member.Name, fields)
                    && Walk(pinned, member.Target, check, entity, inItem, fields);

            case UnaryNode unary:
                return Walk(pinned, unary.Operand, check, entity, inItem, fields);

            case BinaryNode binary:
                return Walk(pinned, binary.Left, check, entity, inItem, fields)
                    && Walk(pinned, binary.Right, check, entity, inItem, fields);

            case IsNullNode isNull:
                return Walk(pinned, isNull.Operand, check, entity, inItem, fields);

            case InNode inList:
                return Walk(pinned, inList.Operand, check, entity, inItem, fields)
                    && inList.Items.All(item => Walk(pinned, item, check, entity, inItem, fields));

            case CallNode call:
                return WalkCall(pinned, call, check, entity, inItem, fields);

            default:
                return false;
        }
    }

    private static bool WalkCall(ApplicationModel pinned, CallNode call, ExpressionCheckResult check, EntityModel entity, bool inItem, HashSet<MappedField> fields)
    {
        // A rule body sees only its parameters, so the fields it reads are the arguments at this call.
        if (check.RuleCalls.ContainsKey(call))
        {
            return call.Arguments.All(argument => Walk(pinned, argument, check, entity, inItem, fields));
        }

        if (_aggregateNames.Contains(call.Name))
        {
            return WalkAggregate(pinned, call, check, entity, inItem, fields);
        }

        return ExpressionFunctions.IsFunction(call.Name)
            && call.Arguments.All(argument => Walk(pinned, argument, check, entity, inItem, fields));
    }

    // The first argument names a child collection of the entity. The second is evaluated once per child row.
    private static bool WalkAggregate(ApplicationModel pinned, CallNode call, ExpressionCheckResult check, EntityModel entity, bool inItem, HashSet<MappedField> fields)
    {
        if (inItem
            || call.Arguments is not [NameNode list, ..]
            || !entity.TryGetField(list.Name, out var collection)
            || collection.Type != FieldType.ChildCollection
            || collection.Target is null
            || pinned.FindEntity(collection.Target.Id) is not { } child)
        {
            return false;
        }

        fields.Add(new MappedField(entity.Id, collection.Name));
        return call.Arguments.Skip(1).All(argument => Walk(pinned, argument, check, child, inItem: true, fields));
    }

    private static bool Add(EntityModel entity, string name, HashSet<MappedField> fields)
    {
        if (!entity.TryGetField(name, out var field))
        {
            return false;
        }

        fields.Add(new MappedField(entity.Id, field.Name));
        return true;
    }
}
