using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled validation rule: its boolean expression parsed and checked against the entity's
/// fields, the text whose key is the message of a failure, and the field a failure is reported on.
/// </summary>
public sealed record ValidationModel
{
    /// <summary>The expression as written.</summary>
    public required string Expression { get; init; }

    public required ExpressionNode Syntax { get; init; }

    /// <summary>The type checker's result, which the interpreter needs.</summary>
    public required ExpressionCheckResult Check { get; init; }

    public required TextReference Message { get; init; }

    /// <summary>The declared name of the field a failure is reported on.</summary>
    public required string Field { get; init; }

    /// <summary>
    /// Parses <paramref name="expression"/> and checks it as a boolean over <paramref name="scope"/>.
    /// The compiler reports every problem first, so a problem here throws <see cref="ArgumentException"/>.
    /// </summary>
    public static ValidationModel Compile(string expression, ExpressionScope scope, TextReference message, string field)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(field);

        var parsed = ExpressionParser.Parse(expression);
        if (!parsed.Succeeded)
        {
            throw new ArgumentException($"The validation does not parse: {parsed.Diagnostic.Message}", nameof(expression));
        }

        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean);
        if (!check.Succeeded)
        {
            throw new ArgumentException($"The validation does not type-check: {check.Diagnostic.Message}", nameof(expression));
        }

        return new ValidationModel
        {
            Expression = expression,
            Syntax = parsed.Expression,
            Check = check,
            Message = message,
            Field = field,
        };
    }
}
