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
    /// <paramref name="scope"/>. Only a policy filter can call <c>currentUser()</c>, and only a module
    /// contract's expression, with <paramref name="contract"/>, can call <c>previous</c>. The compiler
    /// reports every problem first, so a problem here throws <see cref="ArgumentException"/>.
    /// </summary>
    public static ExpressionModel Compile(string expression, ExpressionScope scope, ExpressionType expected, bool policyFilter = false, bool contract = false)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expected);

        var parsed = ExpressionParser.Parse(expression);
        if (!parsed.Succeeded)
        {
            throw new ArgumentException($"The expression does not parse: {parsed.Diagnostic.Message}", nameof(expression));
        }

        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, expected, policyFilter: policyFilter, contract: contract);
        if (!check.Succeeded)
        {
            throw new ArgumentException($"The expression does not type-check: {check.Diagnostic.Message}", nameof(expression));
        }

        return new ExpressionModel { Expression = expression, Syntax = parsed.Expression, Check = check };
    }

    /// <summary>
    /// <paramref name="left"/> <c>and</c> <paramref name="right"/>, as one boolean expression that
    /// reuses both syntax trees and merges their check results.
    /// </summary>
    public static ExpressionModel And(ExpressionModel left, ExpressionModel right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return Join(BinaryOperator.And, "and", [left, right]);
    }

    /// <summary>
    /// The <c>or</c> of <paramref name="items"/>, as one boolean expression that reuses their
    /// syntax trees and merges their check results. One item gives itself.
    /// </summary>
    public static ExpressionModel Or(IReadOnlyList<ExpressionModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfZero(items.Count);

        return items.Count == 1 ? items[0] : Join(BinaryOperator.Or, "or", items);
    }

    private static ExpressionModel Join(BinaryOperator @operator, string keyword, IReadOnlyList<ExpressionModel> items)
    {
        var syntax = items[0].Syntax;
        for (var index = 1; index < items.Count; index++)
        {
            syntax = new BinaryNode(0, @operator, syntax, items[index].Syntax);
        }

        return new ExpressionModel
        {
            Expression = string.Join($" {keyword} ", items.Select(item => $"({item.Expression})")),
            Syntax = syntax,
            Check = ExpressionCheckResult.Merge(items.Select(item => item.Check)),
        };
    }
}
