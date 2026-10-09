using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Model;

/// <summary>A compiled expression: its text, its syntax tree and the type checker's result.</summary>
public sealed record ExpressionModel
{
    /// <summary>The expression as written.</summary>
    public required string Expression { get; init; }

    public required ExpressionNode Syntax { get; init; }

    /// <summary>The type checker's result, which the interpreter needs.</summary>
    public required ExpressionCheckResult Check { get; init; }

    /// <summary>
    /// Parses <paramref name="expression"/> and checks it against <paramref name="expected"/> over
    /// <paramref name="scope"/>. The compiler reports every problem first, so a problem here throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public static ExpressionModel Compile(string expression, ExpressionScope scope, ExpressionType expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expected);

        var parsed = ExpressionParser.Parse(expression);
        if (!parsed.Succeeded)
        {
            throw new ArgumentException($"The expression does not parse: {parsed.Diagnostic.Message}", nameof(expression));
        }

        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, expected);
        if (!check.Succeeded)
        {
            throw new ArgumentException($"The expression does not type-check: {check.Diagnostic.Message}", nameof(expression));
        }

        return new ExpressionModel { Expression = expression, Syntax = parsed.Expression, Check = check };
    }
}
