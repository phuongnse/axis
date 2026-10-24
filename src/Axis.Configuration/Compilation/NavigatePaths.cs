using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Finds the reference paths a checked navigate expression reads, such as <c>department.name</c>,
/// and writes them with the declared field names.
/// </summary>
internal static class NavigatePaths
{
    /// <summary>
    /// Each path of <paramref name="expression"/> from the entity <paramref name="root"/>, in the order
    /// it is read, as dotted declared field names. A name with no step after it is not a path. The
    /// checker has accepted the expression, so every path resolves.
    /// </summary>
    public static IEnumerable<string> Of(string expression, EntityResource root, Func<string, EntityResource?> findEntity)
    {
        var parsed = ExpressionParser.Parse(expression);
        if (!parsed.Succeeded)
        {
            yield break;
        }

        foreach (var chain in Chains(parsed.Expression!))
        {
            var problem = ApplicationCompiler.ResolvePath(root, string.Join('.', chain), findEntity, _ => { }, out var hops);
            if (problem is null && hops.Count > 0)
            {
                yield return string.Join('.', hops.Select(hop => hop.Name));
            }
        }
    }

    /// <summary>The names of each outermost path of the expression, such as <c>department, name</c>.</summary>
    private static IEnumerable<List<string>> Chains(ExpressionNode node)
    {
        switch (node)
        {
            case MemberNode member:
                var names = new List<string>();
                ExpressionNode current = member;
                while (current is MemberNode step)
                {
                    names.Insert(0, step.Name);
                    current = step.Target;
                }

                if (current is NameNode root)
                {
                    names.Insert(0, root.Name);
                    yield return names;
                }

                break;

            case UnaryNode unary:
                foreach (var chain in Chains(unary.Operand))
                {
                    yield return chain;
                }

                break;

            case BinaryNode binary:
                foreach (var chain in Chains(binary.Left).Concat(Chains(binary.Right)))
                {
                    yield return chain;
                }

                break;

            case IsNullNode isNull:
                foreach (var chain in Chains(isNull.Operand))
                {
                    yield return chain;
                }

                break;

            case InNode inList:
                foreach (var chain in Chains(inList.Operand).Concat(inList.Items.SelectMany(Chains)))
                {
                    yield return chain;
                }

                break;

            case CallNode call:
                foreach (var chain in call.Arguments.SelectMany(Chains))
                {
                    yield return chain;
                }

                break;
        }
    }
}
