using Axis.Expressions.Diagnostics;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Parsing;

/// <summary>
/// Parses expression text into a syntax tree, following the grammar in
/// docs/reference/expressions.md. It stops at the first problem and reports only that one. It
/// never throws for any input: recursion is bounded by <see cref="ExpressionLimits.MaxDepth"/>.
/// </summary>
public static class ExpressionParser
{
    public static ExpressionParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > ExpressionLimits.MaxLength)
        {
            return new ExpressionParseResult(null, new ExpressionDiagnostic(
                ExpressionDiagnosticCodes.TooLong,
                $"The expression has {text.Length} characters; the limit is {ExpressionLimits.MaxLength}.",
                ExpressionLimits.MaxLength));
        }

        try
        {
            return new ExpressionParseResult(new Parser(text).ParseAll(), null);
        }
        catch (ParseFailure failure)
        {
            return new ExpressionParseResult(null, failure.Diagnostic);
        }
    }

    /// <summary>A parsed node with the height of its subtree, counting parentheses as a level.</summary>
    private readonly record struct Parsed(ExpressionNode Node, int Height);

    /// <summary>One recursive-descent method per grammar rule.</summary>
    private sealed class Parser
    {
        private readonly Tokenizer _tokenizer;
        private Token _current;
        private int _open;
        private int _nodes;

        public Parser(string text)
        {
            _tokenizer = new Tokenizer(text);
            _current = _tokenizer.Next();
        }

        public ExpressionNode ParseAll()
        {
            var expression = ParseExpression();
            if (_current.Kind != TokenKind.End)
            {
                throw Unexpected();
            }

            return expression.Node;
        }

        private Parsed ParseExpression() => ParseOr();

        private Parsed ParseOr()
        {
            var left = ParseAnd();
            while (_current.Kind == TokenKind.Or)
            {
                var op = Advance();
                var right = ParseAnd();
                left = Binary(op, BinaryOperator.Or, left, right);
            }

            return left;
        }

        private Parsed ParseAnd()
        {
            var left = ParseNot();
            while (_current.Kind == TokenKind.And)
            {
                var op = Advance();
                var right = ParseNot();
                left = Binary(op, BinaryOperator.And, left, right);
            }

            return left;
        }

        private Parsed ParseNot()
        {
            if (_current.Kind != TokenKind.Not)
            {
                return ParseComparison();
            }

            var op = Advance();
            Enter(op);
            var operand = ParseNot();
            _open--;
            return Node(new UnaryNode(op.Offset, UnaryOperator.Not, operand.Node), operand.Height + 1);
        }

        private Parsed ParseComparison()
        {
            var left = ParseAdditive();
            if (CompareOperator(_current.Kind) is { } compare)
            {
                var op = Advance();
                var right = ParseAdditive();
                return Binary(op, compare, left, right);
            }

            if (_current.Kind == TokenKind.Is)
            {
                var op = Advance();
                var negated = _current.Kind == TokenKind.Not;
                if (negated)
                {
                    Advance();
                }

                Expect(TokenKind.Null, "'null'");
                return Node(new IsNullNode(op.Offset, left.Node, negated), left.Height + 1);
            }

            if (_current.Kind == TokenKind.In)
            {
                return ParseIn(left);
            }

            return left;
        }

        private Parsed ParseIn(Parsed operand)
        {
            var op = Advance();
            Expect(TokenKind.OpenParen, "'('");
            if (_current.Kind == TokenKind.CloseParen)
            {
                throw Failure(_current.Offset, $"The list after 'in' is empty at character {_current.Offset + 1}. Give at least one item.");
            }

            var items = new List<ExpressionNode>();
            var height = operand.Height;
            while (true)
            {
                var item = ParseItem();
                items.Add(item.Node);
                height = Math.Max(height, item.Height);
                if (_current.Kind != TokenKind.Comma)
                {
                    break;
                }

                Advance();
            }

            Expect(TokenKind.CloseParen, "',' or ')'");
            return Node(new InNode(op.Offset, operand.Node, items), height + 1);
        }

        /// <summary>An <c>in</c> item: a literal other than null, or <c>date</c> or <c>dateTime</c> on a text literal.</summary>
        private Parsed ParseItem()
        {
            var token = _current;
            switch (token.Kind)
            {
                case TokenKind.Integer or TokenKind.Decimal or TokenKind.Text or TokenKind.True or TokenKind.False:
                    return ParsePrimary();
                case TokenKind.Name when IsDateFunction(token.Text!):
                    Advance();
                    if (_current.Kind != TokenKind.OpenParen)
                    {
                        break;
                    }

                    Advance();
                    var argument = _current;
                    Expect(TokenKind.Text, "a text literal");
                    Expect(TokenKind.CloseParen, "')'");
                    var text = Node(new TextLiteral(argument.Offset, argument.Text!), 1);
                    return Node(new CallNode(token.Offset, token.Text!, [text.Node]), 2);
            }

            throw Failure(token.Offset,
                $"An 'in' item must be a literal other than null, or date('…') or dateTime('…') on a text literal, at character {token.Offset + 1}.");
        }

        private Parsed ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (_current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Advance();
                var right = ParseMultiplicative();
                left = Binary(op, op.Kind == TokenKind.Plus ? BinaryOperator.Add : BinaryOperator.Subtract, left, right);
            }

            return left;
        }

        private Parsed ParseMultiplicative()
        {
            var left = ParseUnary();
            while (_current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = Advance();
                var right = ParseUnary();
                left = Binary(op, op.Kind == TokenKind.Star ? BinaryOperator.Multiply : BinaryOperator.Divide, left, right);
            }

            return left;
        }

        private Parsed ParseUnary()
        {
            if (_current.Kind != TokenKind.Minus)
            {
                return ParsePath();
            }

            var op = Advance();
            Enter(op);
            var operand = ParseUnary();
            _open--;
            return Node(new UnaryNode(op.Offset, UnaryOperator.Negate, operand.Node), operand.Height + 1);
        }

        private Parsed ParsePath()
        {
            var target = ParsePrimary();
            while (_current.Kind == TokenKind.Dot)
            {
                var dot = Advance();
                var name = _current;
                Expect(TokenKind.Name, "a name");
                target = Node(new MemberNode(dot.Offset, target.Node, name.Text!), target.Height + 1);
            }

            return target;
        }

        private Parsed ParsePrimary()
        {
            var token = _current;
            switch (token.Kind)
            {
                case TokenKind.Integer:
                    Advance();
                    return Node(new IntegerLiteral(token.Offset, token.Integer), 1);
                case TokenKind.Decimal:
                    Advance();
                    return Node(new DecimalLiteral(token.Offset, token.Decimal), 1);
                case TokenKind.Text:
                    Advance();
                    return Node(new TextLiteral(token.Offset, token.Text!), 1);
                case TokenKind.True or TokenKind.False:
                    Advance();
                    return Node(new BooleanLiteral(token.Offset, token.Kind == TokenKind.True), 1);
                case TokenKind.Null:
                    Advance();
                    return Node(new NullLiteral(token.Offset), 1);
                case TokenKind.Name:
                    Advance();
                    return _current.Kind == TokenKind.OpenParen
                        ? ParseCall(token)
                        : Node(new NameNode(token.Offset, token.Text!), 1);
                case TokenKind.OpenParen:
                    Advance();
                    Enter(token);
                    var inner = ParseExpression();
                    Expect(TokenKind.CloseParen, "')'");
                    _open--;
                    return Parenthesised(token, inner);
                case TokenKind.End:
                    throw Failure(token.Offset, $"Expected an expression at character {token.Offset + 1}.");
                default:
                    throw Unexpected();
            }
        }

        private Parsed ParseCall(Token name)
        {
            var open = Advance();
            var arguments = new List<ExpressionNode>();
            var height = 0;
            if (_current.Kind != TokenKind.CloseParen)
            {
                Enter(open);
                while (true)
                {
                    var argument = ParseExpression();
                    arguments.Add(argument.Node);
                    height = Math.Max(height, argument.Height);
                    if (_current.Kind != TokenKind.Comma)
                    {
                        break;
                    }

                    Advance();
                }

                _open--;
            }

            Expect(TokenKind.CloseParen, "',' or ')'");
            return Node(new CallNode(name.Offset, name.Text!, arguments), height + 1);
        }

        private static BinaryOperator? CompareOperator(TokenKind kind) => kind switch
        {
            TokenKind.Equal => BinaryOperator.Equal,
            TokenKind.NotEqual => BinaryOperator.NotEqual,
            TokenKind.Less => BinaryOperator.Less,
            TokenKind.LessOrEqual => BinaryOperator.LessOrEqual,
            TokenKind.Greater => BinaryOperator.Greater,
            TokenKind.GreaterOrEqual => BinaryOperator.GreaterOrEqual,
            _ => null,
        };

        private static bool IsDateFunction(string name) =>
            name.Equals("date", StringComparison.OrdinalIgnoreCase) || name.Equals("dateTime", StringComparison.OrdinalIgnoreCase);

        private Parsed Binary(Token op, BinaryOperator binary, Parsed left, Parsed right) =>
            Node(new BinaryNode(op.Offset, binary, left.Node, right.Node), Math.Max(left.Height, right.Height) + 1);

        /// <summary>Counts a new node and checks the node and depth limits.</summary>
        private Parsed Node(ExpressionNode node, int height)
        {
            if (++_nodes > ExpressionLimits.MaxNodes)
            {
                throw new ParseFailure(new ExpressionDiagnostic(
                    ExpressionDiagnosticCodes.TooManyNodes,
                    $"The expression has more than {ExpressionLimits.MaxNodes} syntax nodes at character {node.Offset + 1}. Move part of it into a named rule.",
                    node.Offset));
            }

            CheckDepth(node.Offset, height);
            return new Parsed(node, height);
        }

        private static Parsed Parenthesised(Token open, Parsed inner)
        {
            CheckDepth(open.Offset, inner.Height + 1);
            return inner with { Height = inner.Height + 1 };
        }

        /// <summary>
        /// Enters a level that adds at least one to the height: a parenthesised expression, a call's
        /// arguments, or the operand of <c>not</c> or unary <c>-</c>. Stopping here bounds the
        /// recursion before the subtree is parsed.
        /// </summary>
        private void Enter(Token token)
        {
            if (++_open >= ExpressionLimits.MaxDepth)
            {
                throw TooDeep(token.Offset);
            }
        }

        private static void CheckDepth(int offset, int height)
        {
            if (height > ExpressionLimits.MaxDepth)
            {
                throw TooDeep(offset);
            }
        }

        private static ParseFailure TooDeep(int offset) =>
            new(new ExpressionDiagnostic(
                ExpressionDiagnosticCodes.TooDeep,
                $"The expression nests deeper than {ExpressionLimits.MaxDepth} levels at character {offset + 1}. Move part of it into a named rule.",
                offset));

        private Token Advance()
        {
            var token = _current;
            _current = _tokenizer.Next();
            return token;
        }

        private void Expect(TokenKind kind, string expected)
        {
            if (_current.Kind != kind)
            {
                var found = _current.Kind == TokenKind.End ? "" : $", found '{_tokenizer.SourceOf(_current)}'";
                throw Failure(_current.Offset, $"Expected {expected} at character {_current.Offset + 1}{found}.");
            }

            Advance();
        }

        private ParseFailure Unexpected() =>
            Failure(_current.Offset, $"Unexpected '{_tokenizer.SourceOf(_current)}' at character {_current.Offset + 1}.");

        private static ParseFailure Failure(int offset, string message) =>
            new(new ExpressionDiagnostic(ExpressionDiagnosticCodes.SyntaxError, message, offset));
    }
}
