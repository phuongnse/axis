using Axis.Expressions.Syntax;

namespace Axis.Expressions.Typing;

/// <summary>One typed parameter of a named rule.</summary>
public sealed record ExpressionRuleParameter(string Name, ExpressionType Type);

/// <summary>
/// A named rule an expression can call like a function: its parameters, result type and body. The
/// body sees only the parameters. <see cref="Body"/> and <see cref="BodyCheck"/> are null for a
/// rule known only by its signature, such as one whose own expression failed or that is in a call
/// cycle. Calls to it are still checked, but the compile then has errors, so it is never evaluated.
/// </summary>
public sealed record ExpressionRule(string Name, IReadOnlyList<ExpressionRuleParameter> Parameters, ExpressionType ResultType)
{
    /// <summary>The parsed body, checked against <see cref="ResultType"/> over the parameters.</summary>
    public ExpressionNode? Body { get; init; }

    /// <summary>The type checker's result for <see cref="Body"/>, which the interpreter needs.</summary>
    public ExpressionCheckResult? BodyCheck { get; init; }
}
