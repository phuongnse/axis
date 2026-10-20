using Axis.Expressions.Parsing;

namespace Axis.Expressions.Diagnostics;

/// <summary>
/// The character range a diagnostic covers, for an editor to underline. A diagnostic carries only one
/// position, so the range is the token at that position. The range comes from the tokenizer, so no
/// syntax node has to know where it ends.
/// </summary>
public static class ExpressionRange
{
    /// <summary>
    /// The UTF-16 range of <paramref name="diagnostic"/> in <paramref name="expression"/>. <c>End</c> is exclusive.
    /// A too long expression and a result of the wrong type cover the whole expression. Otherwise the range is the
    /// token at the diagnostic's offset. A qualified call such as <c>departments:IsManager</c> is one range. A
    /// character that starts no token covers that one character, and text that is never closed runs to the end. A
    /// position that starts no token covers one character.
    /// </summary>
    public static (int Start, int End) Of(string expression, ExpressionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (diagnostic.Code is ExpressionDiagnosticCodes.TooLong or ExpressionDiagnosticCodes.ResultTypeMismatch)
        {
            return (0, expression.Length);
        }

        var offset = Math.Clamp(diagnostic.Offset, 0, expression.Length);
        var (start, end) = TokenAt(expression, offset);
        return (start, Math.Min(end, expression.Length));
    }

    private static (int Start, int End) TokenAt(string expression, int offset)
    {
        var tokenizer = new Tokenizer(expression);
        try
        {
            var token = tokenizer.Next();
            while (token.Offset < offset && token.Kind != TokenKind.End)
            {
                token = tokenizer.Next();
            }

            if (token.Offset != offset)
            {
                return (offset, offset + 1);
            }

            if (token.Kind == TokenKind.End)
            {
                return (expression.Length, expression.Length);
            }

            var end = token.Offset + token.Length;
            if (token.Kind == TokenKind.ModuleQualifier)
            {
                var name = tokenizer.Next();
                if (name.Kind == TokenKind.Name)
                {
                    end = name.Offset + name.Length;
                }
            }

            return (token.Offset, end);
        }
        catch (ParseFailure failure) when (failure.Diagnostic.Offset == offset)
        {
            // Text that is never closed runs to the end. Any other failure is at one character.
            return expression[offset] == '\'' ? (offset, expression.Length) : (offset, offset + 1);
        }
        catch (ParseFailure)
        {
            return (offset, offset + 1);
        }
    }
}
