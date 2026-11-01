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

    /// <summary>
    /// The paths that end at a collection, such as <c>type.steps</c> in <c>count(type.steps)</c>,
    /// compared by reference. Each one is also in <see cref="PathTargets"/>, which names the entity
    /// that holds the collection.
    /// </summary>
    public IReadOnlySet<MemberNode> CollectionPaths { get; internal init; } = new HashSet<MemberNode>(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The result of a boolean expression built from expressions that <paramref name="results"/>
    /// checked. It holds every rule call, path target, collection path and decimal call of them, so the tables
    /// still match the nodes the built expression reuses.
    /// </summary>
    public static ExpressionCheckResult Merge(IEnumerable<ExpressionCheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var decimalCalls = new HashSet<CallNode>(ReferenceEqualityComparer.Instance);
        var ruleCalls = new Dictionary<CallNode, ExpressionRule>(ReferenceEqualityComparer.Instance);
        var pathTargets = new Dictionary<MemberNode, string>(ReferenceEqualityComparer.Instance);
        var collectionPaths = new HashSet<MemberNode>(ReferenceEqualityComparer.Instance);
        foreach (var result in results)
        {
            decimalCalls.UnionWith(result.DecimalCalls);
            collectionPaths.UnionWith(result.CollectionPaths);
            foreach (var (call, rule) in result.RuleCalls)
            {
                ruleCalls[call] = rule;
            }

            foreach (var (step, target) in result.PathTargets)
            {
                pathTargets[step] = target;
            }
        }

        return new ExpressionCheckResult(ExpressionType.Boolean, null)
        {
            DecimalCalls = decimalCalls,
            RuleCalls = ruleCalls,
            PathTargets = pathTargets,
            CollectionPaths = collectionPaths,
        };
    }
}
