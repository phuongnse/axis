using System.Diagnostics.CodeAnalysis;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Typing;

/// <summary>The outcome of type checking one expression. Exactly one of the two values is set.</summary>
public sealed record ExpressionCheckResult(ExpressionType? Type, ExpressionDiagnostic? Diagnostic)
{
    [MemberNotNullWhen(true, nameof(Type))]
    [MemberNotNullWhen(false, nameof(Diagnostic))]
    public bool Succeeded => Diagnostic is null;

    /// <summary>
    /// The <c>coalesce</c> and <c>if</c> calls checked as decimal, compared by reference. The
    /// interpreter returns an integer one of them picks as a decimal, so the run-time type matches.
    /// </summary>
    internal IReadOnlySet<CallNode> DecimalCalls { get; init; } = new HashSet<CallNode>(ReferenceEqualityComparer.Instance);

    /// <summary>The rule each rule call resolved to, keyed by the call and compared by reference.</summary>
    internal IReadOnlyDictionary<CallNode, ExpressionRule> RuleCalls { get; init; } =
        new Dictionary<CallNode, ExpressionRule>(ReferenceEqualityComparer.Instance);
}
