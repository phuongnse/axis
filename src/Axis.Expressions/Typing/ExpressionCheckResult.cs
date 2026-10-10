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
    /// The <c>coalesce</c>, <c>if</c>, <c>sum</c>, <c>min</c> and <c>max</c> calls checked as
    /// decimal, compared by reference. The interpreter returns an integer one of them gives as a
    /// decimal, so the run-time type matches.
    /// </summary>
    internal IReadOnlySet<CallNode> DecimalCalls { get; init; } = new HashSet<CallNode>(ReferenceEqualityComparer.Instance);

    /// <summary>The rule each rule call resolved to, keyed by the call and compared by reference.</summary>
    public IReadOnlyDictionary<CallNode, ExpressionRule> RuleCalls { get; internal init; } =
        new Dictionary<CallNode, ExpressionRule>(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The target entity name of each path step, such as <c>department.manager</c>, keyed by the
    /// step and compared by reference. The name is the reference's target as the scope gives it.
    /// </summary>
    public IReadOnlyDictionary<MemberNode, string> PathTargets { get; internal init; } =
        new Dictionary<MemberNode, string>(ReferenceEqualityComparer.Instance);
}
