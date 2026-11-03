using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;

namespace Axis.Configuration.Compilation;

/// <summary>Reads the field names that an expression starts a path at.</summary>
internal static class ExpressionNames
{
    /// <summary>The names a value starts a field path at, in any order.</summary>
    internal static IEnumerable<string> NamedFields(ExpressionNode node) => node switch
    {
        NameNode name => [name.Name],
        MemberNode member => NamedFields(member.Target),
        CallNode call => call.Arguments.SelectMany(NamedFields),
        UnaryNode unary => NamedFields(unary.Operand),
        BinaryNode binary => NamedFields(binary.Left).Concat(NamedFields(binary.Right)),
        IsNullNode isNull => NamedFields(isNull.Operand),
        InNode inNode => NamedFields(inNode.Operand).Concat(inNode.Items.SelectMany(NamedFields)),
        _ => [],
    };

    /// <summary>The names in an expression's text, or none when the text is missing or does not parse.</summary>
    internal static IEnumerable<string> NamedFields(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parsed = ExpressionParser.Parse(text);
        return parsed.Succeeded ? NamedFields(parsed.Expression!) : [];
    }
}
