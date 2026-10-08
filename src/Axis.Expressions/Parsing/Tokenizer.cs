using System.Globalization;
using Axis.Expressions.Diagnostics;

namespace Axis.Expressions.Parsing;

/// <summary>
/// Reads the tokens of an expression one at a time, so the first problem in text order is the
/// one reported.
/// </summary>
internal sealed class Tokenizer(string text)
{
    private static readonly Dictionary<string, TokenKind> _keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["and"] = TokenKind.And,
        ["or"] = TokenKind.Or,
        ["not"] = TokenKind.Not,
        ["is"] = TokenKind.Is,
        ["in"] = TokenKind.In,
        ["null"] = TokenKind.Null,
        ["true"] = TokenKind.True,
        ["false"] = TokenKind.False,
    };

    private readonly string _text = text;
    private int _position;

    public Token Next()
    {
        while (_position < _text.Length && _text[_position] is ' ' or '\t' or '\r' or '\n')
        {
            _position++;
        }

        if (_position >= _text.Length)
        {
            return new Token(TokenKind.End, _text.Length, 0);
        }

        var start = _position;
        var c = _text[start];
        if (char.IsAsciiLetter(c))
        {
            return ReadName(start);
        }

        if (char.IsAsciiDigit(c))
        {
            return ReadNumber(start);
        }

        if (c == '\'')
        {
            return ReadText(start);
        }

        var next = start + 1 < _text.Length ? _text[start + 1] : '\0';
        switch (c)
        {
            case '=' when next == '=':
                return Operator(TokenKind.Equal, start, 2);
            case '!' when next == '=':
                return Operator(TokenKind.NotEqual, start, 2);
            case '<' when next == '=':
                return Operator(TokenKind.LessOrEqual, start, 2);
            case '>' when next == '=':
                return Operator(TokenKind.GreaterOrEqual, start, 2);
            case '<':
                return Operator(TokenKind.Less, start, 1);
            case '>':
                return Operator(TokenKind.Greater, start, 1);
            case '+':
                return Operator(TokenKind.Plus, start, 1);
            case '-':
                return Operator(TokenKind.Minus, start, 1);
            case '*':
                return Operator(TokenKind.Star, start, 1);
            case '/':
                return Operator(TokenKind.Slash, start, 1);
            case '(':
                return Operator(TokenKind.OpenParen, start, 1);
            case ')':
                return Operator(TokenKind.CloseParen, start, 1);
            case ',':
                return Operator(TokenKind.Comma, start, 1);
            case '.':
                return Operator(TokenKind.Dot, start, 1);
            case '=':
                throw Failure(start, $"Unexpected '=' at character {start + 1}. Use '==' to compare.");
            case '!':
                throw Failure(start, $"Unexpected '!' at character {start + 1}. Use 'not' or '!='.");
            default:
                throw Failure(start, $"Unexpected character {DescribeCharacter(c)} at character {start + 1}.");
        }
    }

    /// <summary>The source text of a token, for messages. Long tokens are shortened.</summary>
    public string SourceOf(Token token) => Shorten(_text.AsSpan(token.Offset, token.Length));

    private Token Operator(TokenKind kind, int start, int length)
    {
        _position = start + length;
        return new Token(kind, start, length);
    }

    private Token ReadName(int start)
    {
        while (_position < _text.Length && char.IsAsciiLetterOrDigit(_text[_position]))
        {
            _position++;
        }

        var name = _text[start.._position];
        return _keywords.TryGetValue(name, out var keyword)
            ? new Token(keyword, start, name.Length)
            : new Token(TokenKind.Name, start, name.Length, Text: name);
    }

    private Token ReadNumber(int start)
    {
        SkipDigits();
        var isDecimal = _position + 1 < _text.Length && _text[_position] == '.' && char.IsAsciiDigit(_text[_position + 1]);
        if (!isDecimal)
        {
            var digits = _text.AsSpan(start, _position - start);
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
            {
                throw Failure(start, $"The integer {Shorten(digits)} at character {start + 1} is too large. The largest integer is {long.MaxValue}.");
            }

            return new Token(TokenKind.Integer, start, digits.Length, Integer: integer);
        }

        var point = _position;
        _position++;
        SkipDigits();
        var source = _text.AsSpan(start, _position - start);
        if (!decimal.TryParse(source, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || !HoldsExactly(value, _text.AsSpan(start, point - start), _text.AsSpan(point + 1, _position - point - 1)))
        {
            throw Failure(start, $"The decimal {Shorten(source)} at character {start + 1} has too many digits to be kept exactly.");
        }

        return new Token(TokenKind.Decimal, start, source.Length, Decimal: value);
    }

    /// <summary>
    /// Whether the parsed value has every digit as written. Parsing rounds a literal with more
    /// digits than a decimal can hold, and a literal must be kept exactly.
    /// </summary>
    private static bool HoldsExactly(decimal value, ReadOnlySpan<char> whole, ReadOnlySpan<char> fraction)
    {
        whole = whole.TrimStart('0');
        var expected = string.Concat(whole.IsEmpty ? "0" : whole, ".", fraction);
        return value.ToString(CultureInfo.InvariantCulture) == expected;
    }

    private Token ReadText(int start)
    {
        var value = new System.Text.StringBuilder();
        _position++;
        while (_position < _text.Length)
        {
            var c = _text[_position++];
            if (c != '\'')
            {
                value.Append(c);
            }
            else if (_position < _text.Length && _text[_position] == '\'')
            {
                value.Append('\'');
                _position++;
            }
            else
            {
                return new Token(TokenKind.Text, start, _position - start, Text: value.ToString());
            }
        }

        throw Failure(start, $"Text is not closed at character {start + 1}. End it with a single quote.");
    }

    private void SkipDigits()
    {
        while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
        {
            _position++;
        }
    }

    private static string Shorten(ReadOnlySpan<char> source)
    {
        const int MaxShown = 30;
        return source.Length <= MaxShown ? source.ToString() : string.Concat(source[..MaxShown], "…");
    }

    private static string DescribeCharacter(char c) =>
        char.IsControl(c) || char.IsWhiteSpace(c) || char.IsSurrogate(c)
            ? string.Create(CultureInfo.InvariantCulture, $"U+{(int)c:X4}")
            : $"'{c}'";

    private static ParseFailure Failure(int offset, string message) =>
        new(new ExpressionDiagnostic(ExpressionDiagnosticCodes.SyntaxError, message, offset));
}
