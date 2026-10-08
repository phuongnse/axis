using System.Globalization;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Tests;

/// <summary>Prints a syntax tree as an S-expression, such as <c>(and (== status 'submitted') x)</c>.</summary>
public static class TreePrinter
{
    public static string Print(ExpressionNode node) => node switch
    {
        IntegerLiteral literal => literal.Value.ToString(CultureInfo.InvariantCulture),
        DecimalLiteral literal => literal.Value.ToString(CultureInfo.InvariantCulture),
        TextLiteral literal => $"'{literal.Value.Replace("'", "''", StringComparison.Ordinal)}'",
        BooleanLiteral literal => literal.Value ? "true" : "false",
        NullLiteral => "null",
        NameNode name => name.Name,
        MemberNode member => $"(. {Print(member.Target)} {member.Name})",
        CallNode call => List("call", [call.Name, .. call.Arguments.Select(Print)]),
        UnaryNode unary => List(unary.Operator == UnaryOperator.Not ? "not" : "neg", [Print(unary.Operand)]),
        BinaryNode binary => List(Symbol(binary.Operator), [Print(binary.Left), Print(binary.Right)]),
        IsNullNode isNull => List(isNull.Negated ? "is-not-null" : "is-null", [Print(isNull.Operand)]),
        InNode inNode => List("in", [Print(inNode.Operand), .. inNode.Items.Select(Print)]),
        _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, "Unknown node type."),
    };

    private static string List(string head, IEnumerable<string> items) => $"({head} {string.Join(' ', items)})";

    private static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Or => "or",
        BinaryOperator.And => "and",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };
}
