using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Tests;

public sealed class ExpressionParserTests
{
    [Theory]
    // Examples from docs/reference/expressions.md.
    [InlineData("status in ('submitted', 'approved')", "(in status 'submitted' 'approved')")]
    [InlineData("quantity is null or quantity > 0", "(or (is-null quantity) (> quantity 0))")]
    [InlineData("sum(lines, quantity * unitPrice)", "(call sum lines (* quantity unitPrice))")]
    [InlineData("department.name", "(. department name)")]
    [InlineData("isLargeRequest(total)", "(call isLargeRequest total)")]
    [InlineData("not x in ('a', 'b')", "(not (in x 'a' 'b'))")]
    [InlineData("a < b and b < c", "(and (< a b) (< b c))")]
    [InlineData("status != previousStatus", "(!= status previousStatus)")]
    [InlineData("quantity * unitPrice", "(* quantity unitPrice)")]
    [InlineData("status == 'submitted' and total >= 10000", "(and (== status 'submitted') (>= total 10000))")]
    [InlineData("quantity > 0", "(> quantity 0)")]
    [InlineData("x == null", "(== x null)")]
    [InlineData("not (x in ('a'))", "(not (in x 'a'))")]
    // Literals.
    [InlineData("42", "42")]
    [InlineData("12.50", "12.50")]
    [InlineData("'submitted'", "'submitted'")]
    [InlineData("'it''s'", "'it''s'")]
    [InlineData("''", "''")]
    [InlineData("true", "true")]
    [InlineData("FALSE", "false")]
    [InlineData("null", "null")]
    [InlineData("date('2026-10-08')", "(call date '2026-10-08')")]
    [InlineData("dateTime('2026-10-08T09:30:00Z')", "(call dateTime '2026-10-08T09:30:00Z')")]
    [InlineData("x in (1, 2.5, 'a', true, false, date('2026-10-08'), DATETIME('2026-10-08T09:30:00Z'))",
        "(in x 1 2.5 'a' true false (call date '2026-10-08') (call DATETIME '2026-10-08T09:30:00Z'))")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    // A keyword in any letter case is never a name.
    [InlineData("Null + 1", "(+ null 1)")]
    [InlineData("a AND b Or NOT c", "(or (and a b) (not c))")]
    // Precedence and associativity.
    [InlineData("a or b and c", "(or a (and b c))")]
    [InlineData("a and b or c", "(or (and a b) c)")]
    [InlineData("not a == b", "(not (== a b))")]
    [InlineData("not not a", "(not (not a))")]
    [InlineData("a + b * c", "(+ a (* b c))")]
    [InlineData("a * b + c / d", "(+ (* a b) (/ c d))")]
    [InlineData("-a.b", "(neg (. a b))")]
    [InlineData("-a * b", "(* (neg a) b)")]
    [InlineData("- -1", "(neg (neg 1))")]
    [InlineData("(a + b) * c", "(* (+ a b) c)")]
    [InlineData("a - b - c", "(- (- a b) c)")]
    [InlineData("a / b / c", "(/ (/ a b) c)")]
    [InlineData("a + b == c * d", "(== (+ a b) (* c d))")]
    [InlineData("x is not null", "(is-not-null x)")]
    [InlineData("x + 1 IS NULL", "(is-null (+ x 1))")]
    [InlineData("a.b.c", "(. (. a b) c)")]
    [InlineData("f(a).b", "(. (call f a) b)")]
    [InlineData("f()", "(call f)")]
    [InlineData("count (lines)", "(call count lines)")]
    [InlineData("1.x", "(. 1 x)")]
    [InlineData("a<=b", "(<= a b)")]
    // Whitespace: space, tab and line breaks, also inside text.
    [InlineData("quantity is null\r\n\tor quantity > 0", "(or (is-null quantity) (> quantity 0))")]
    [InlineData("'line\none'", "'line\none'")]
    public void Expressions_parse_to_the_expected_tree(string text, string expected)
    {
        var result = ExpressionParser.Parse(text);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, TreePrinter.Print(result.Expression));
    }

    [Fact]
    public void Doubled_quotes_in_text_stand_for_one_quote()
    {
        var result = ExpressionParser.Parse("'it''s'");

        Assert.True(result.Succeeded);
        Assert.Equal("it's", Assert.IsType<TextLiteral>(result.Expression).Value);
    }

    [Fact]
    public void Literals_keep_their_value_type_and_scale()
    {
        var integer = ExpressionParser.Parse("42").Expression;
        var number = ExpressionParser.Parse("12.50").Expression;

        Assert.Equal(42L, Assert.IsType<IntegerLiteral>(integer).Value);
        var value = Assert.IsType<DecimalLiteral>(number).Value;
        Assert.Equal(12.50m, value);
        Assert.Equal(2, value.Scale);
    }

    [Fact]
    public void Each_node_points_at_its_defining_token()
    {
        // 0         1         2
        // 0123456789012345678901234
        // a.b + f(x) is null or -y
        var result = ExpressionParser.Parse("a.b + f(x) is null or -y");

        Assert.True(result.Succeeded);
        var or = Assert.IsType<BinaryNode>(result.Expression);
        Assert.Equal(19, or.Offset);
        var isNull = Assert.IsType<IsNullNode>(or.Left);
        Assert.Equal(11, isNull.Offset);
        var add = Assert.IsType<BinaryNode>(isNull.Operand);
        Assert.Equal(4, add.Offset);
        var member = Assert.IsType<MemberNode>(add.Left);
        Assert.Equal(1, member.Offset);
        Assert.Equal(0, member.Target.Offset);
        var call = Assert.IsType<CallNode>(add.Right);
        Assert.Equal(6, call.Offset);
        Assert.Equal(8, call.Arguments[0].Offset);
        var negate = Assert.IsType<UnaryNode>(or.Right);
        Assert.Equal(22, negate.Offset);
        Assert.Equal(23, negate.Operand.Offset);
    }

    [Fact]
    public void Names_are_kept_as_written()
    {
        var result = ExpressionParser.Parse("Department.Name2");

        var member = Assert.IsType<MemberNode>(result.Expression);
        Assert.Equal("Name2", member.Name);
        Assert.Equal("Department", Assert.IsType<NameNode>(member.Target).Name);
    }

    [Theory]
    [InlineData("a < b < c", 6)]
    [InlineData("a == b != c", 7)]
    [InlineData("x is null is null", 10)]
    [InlineData("x in ()", 6)]
    [InlineData("x in (y)", 6)]
    [InlineData("x in (null)", 6)]
    [InlineData("x in (-1)", 6)]
    [InlineData("x in (date)", 6)]
    [InlineData("x in (date(y))", 11)]
    [InlineData("x in (f('a'))", 6)]
    [InlineData("x in (1 2)", 8)]
    [InlineData("x in 1", 5)]
    [InlineData("'abc", 0)]
    [InlineData("a == 'it''s", 5)]
    [InlineData("a +", 3)]
    [InlineData(")", 0)]
    [InlineData("a)", 1)]
    [InlineData("(a", 2)]
    [InlineData("f(a b)", 4)]
    [InlineData("f(a,)", 4)]
    [InlineData("a == = b", 5)]
    [InlineData("a = b", 2)]
    [InlineData("a.f()", 3)]
    [InlineData("a.", 2)]
    [InlineData("a.1", 2)]
    [InlineData("a ! b", 2)]
    [InlineData("a # b", 2)]
    [InlineData("a_b", 1)]
    [InlineData("a b", 1)]
    [InlineData("café", 3)]
    [InlineData("", 0)]
    [InlineData("   ", 3)]
    [InlineData("9223372036854775808", 0)]
    [InlineData("x + 99999999999999999999999999999.5", 4)]
    [InlineData("1.00000000000000000000000000001", 0)]
    [InlineData("x is not 1", 9)]
    [InlineData("x.Null", 2)]
    [InlineData("Null(1)", 4)]
    [InlineData("not", 3)]
    [InlineData("1 2", 2)]
    public void A_syntax_error_gives_one_diagnostic_at_its_offset(string text, int offset)
    {
        var result = ExpressionParser.Parse(text);

        Assert.False(result.Succeeded);
        Assert.Null(result.Expression);
        Assert.Equal(ExpressionDiagnosticCodes.SyntaxError, result.Diagnostic.Code);
        Assert.Equal(offset, result.Diagnostic.Offset);
        Assert.Contains($"at character {offset + 1}", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", result.Diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a < b < c", "Unexpected '<' at character 7.")]
    [InlineData(")", "Unexpected ')' at character 1.")]
    [InlineData("", "Expected an expression at character 1.")]
    [InlineData("(a + b", "Expected ')' at character 7.")]
    [InlineData("(a + b c", "Expected ')' at character 8, found 'c'.")]
    [InlineData("x == 'abc", "Text is not closed at character 6. End it with a single quote.")]
    [InlineData("x in ()", "The list after 'in' is empty at character 7. Give at least one item.")]
    [InlineData("a == = b", "Unexpected '=' at character 6. Use '==' to compare.")]
    [InlineData("a # b", "Unexpected character '#' at character 3.")]
    public void Syntax_error_messages_name_the_position_and_the_token(string text, string message)
    {
        var result = ExpressionParser.Parse(text);

        Assert.Equal(message, result.Diagnostic?.Message);
    }

    [Fact]
    public void An_expression_of_the_maximum_length_parses()
    {
        var text = "'" + new string('a', ExpressionLimits.MaxLength - 2) + "'";

        Assert.Equal(2000, text.Length);
        Assert.True(ExpressionParser.Parse(text).Succeeded);
    }

    [Fact]
    public void An_expression_over_the_maximum_length_is_rejected()
    {
        var text = "'" + new string('a', ExpressionLimits.MaxLength - 1) + "'";

        var result = ExpressionParser.Parse(text);

        Assert.Equal(2001, text.Length);
        Assert.Equal(new ExpressionDiagnostic(
            ExpressionDiagnosticCodes.TooLong, "The expression has 2001 characters; the limit is 2000.", 2000), result.Diagnostic);
    }

    [Fact]
    public void An_expression_of_the_maximum_depth_parses()
    {
        Assert.True(ExpressionParser.Parse(new string('-', 31) + "1").Succeeded);
        Assert.True(ExpressionParser.Parse(new string('(', 31) + "a" + new string(')', 31)).Succeeded);
        Assert.True(ExpressionParser.Parse(Nested("f(", 32) + new string(')', 32)).Succeeded);
        Assert.True(ExpressionParser.Parse(string.Join(" + ", Enumerable.Repeat("a", 32))).Succeeded);
    }

    [Theory]
    [MemberData(nameof(TooDeepExpressions))]
    public void An_expression_over_the_maximum_depth_is_rejected(string text, int offset)
    {
        var result = ExpressionParser.Parse(text);

        Assert.NotNull(result.Diagnostic);
        Assert.Equal(ExpressionDiagnosticCodes.TooDeep, result.Diagnostic.Code);
        Assert.Equal(offset, result.Diagnostic.Offset);
    }

    public static TheoryData<string, int> TooDeepExpressions() => new()
    {
        { new string('-', 32) + "1", 31 },
        { new string('(', 32) + "a" + new string(')', 32), 31 },
        { Nested("not ", 32) + "a", 124 },
        { Nested("f(", 33) + new string(')', 33), 63 },
        // 33 operands make a left-leaning chain of height 33. The 32nd '+' passes the limit.
        { string.Join(" + ", Enumerable.Repeat("a", 33)), 126 },
        { new string('(', 31) + "a + a" + new string(')', 31), 0 },
        { new string('(', 1000) + "a", 31 },
        { Nested("not ", 499) + "a", 124 },
        { Nested("f(", 1000), 63 },
    };

    [Fact]
    public void An_expression_with_the_maximum_node_count_parses()
    {
        // x, 498 items and the 'in' node make 500 nodes.
        Assert.True(ExpressionParser.Parse(InList(498)).Succeeded);
    }

    [Fact]
    public void An_expression_over_the_maximum_node_count_is_rejected()
    {
        var result = ExpressionParser.Parse(InList(499));

        Assert.NotNull(result.Diagnostic);
        Assert.Equal(ExpressionDiagnosticCodes.TooManyNodes, result.Diagnostic.Code);
        Assert.Equal(2, result.Diagnostic.Offset);
    }

    [Fact]
    public void Path_steps_and_call_arguments_count_as_nodes()
    {
        // The call, 499 arguments and 1 extra path step make 501 nodes.
        var arguments = string.Join(",", Enumerable.Repeat("1", 499));

        Assert.True(ExpressionParser.Parse($"f({arguments})").Succeeded);
        Assert.Equal(ExpressionDiagnosticCodes.TooManyNodes, ExpressionParser.Parse($"f({arguments}).a").Diagnostic?.Code);
    }

    [Fact]
    public void The_parser_never_throws()
    {
        string[] pieces =
        [
            "a", "b1", "f", "(", ")", "(", ")", ",", ".", "-", "-", "+", "*", "/", "not", "and", "or", "is", "in",
            "null", "true", "==", "!=", "<", "<=", ">", ">=", "=", "!", "1", "2.5", "99999999999999999999", "'x'",
            "'", "''", "date", " ", "\n", "#", "é", "\ud83d",
        ];
        var random = new Random(101);
        for (var i = 0; i < 20_000; i++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(_ => pieces[random.Next(pieces.Length)] + " "));

            var result = ExpressionParser.Parse(text);

            Assert.True(result.Expression is null != result.Diagnostic is null, text);
        }
    }

    private static string Nested(string prefix, int count) => string.Concat(Enumerable.Repeat(prefix, count));

    private static string InList(int items) => $"x in ({string.Join(",", Enumerable.Repeat("1", items))})";
}
