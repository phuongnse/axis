using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Model;

/// <summary>
/// The expression of a computed field, parsed and checked against the entity's input fields. Its
/// result fits the field's type, and the server stores it on every write of the record.
/// </summary>
public sealed record ComputedFieldModel
{
    /// <summary>The expression as written.</summary>
    public required string Expression { get; init; }

    public required ExpressionNode Syntax { get; init; }

    /// <summary>The type checker's result, which the interpreter needs.</summary>
    public required ExpressionCheckResult Check { get; init; }

    /// <summary>
    /// Parses <paramref name="expression"/> and checks it over <paramref name="scope"/> against the
    /// field's <paramref name="expected"/> type. The compiler reports every problem first, so a
    /// problem here throws <see cref="ArgumentException"/>.
    /// </summary>
    public static ComputedFieldModel Compile(string expression, ExpressionScope scope, ExpressionType expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expected);

        var parsed = ExpressionParser.Parse(expression);
        if (!parsed.Succeeded)
        {
            throw new ArgumentException($"The computed field does not parse: {parsed.Diagnostic.Message}", nameof(expression));
        }

        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, expected);
        if (!check.Succeeded)
        {
            throw new ArgumentException($"The computed field does not type-check: {check.Diagnostic.Message}", nameof(expression));
        }

        return new ComputedFieldModel
        {
            Expression = expression,
            Syntax = parsed.Expression,
            Check = check,
        };
    }
}
