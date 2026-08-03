using Axis.Rules.Domain;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;

namespace Axis.Rules.Application;

internal sealed class OptionalBoundDisplayRewrite : IRuleConditionDisplayRewrite
{
    public RuleDisplayNode? TryRewrite(RuleDisplayNode node) =>
        node is RuleDisplayGroup group
            ? Match(group)
            : null;

    private static RuleDisplayNode? Match(RuleDisplayGroup group)
    {
        (RuleDisplayPredicate? Guard, RuleDisplayNode Node)[] parts = group.Children
            .Select(child => (Guard: OptionalGuard(child), Node: child))
            .ToArray();
        RuleDisplayPredicate[] guards = parts
            .Where(part => part.Guard is not null)
            .Select(part => part.Guard!)
            .ToArray();
        RuleDisplayNode[] consequents = parts
            .Where(part => part.Guard is null)
            .Select(part => part.Node)
            .ToArray();

        return (group.Operator, guards.Length, consequents.Length) switch
        {
            (DomainLogicalOperator.Any, > 0, 1)
                when IsSupportedConsequence(consequents[0]) &&
                     guards.All(guard =>
                         guard.Predicate.Left.Reference is { } reference &&
                         ReferencesInput(consequents[0], reference))
                => new RuleDisplayConditional(
                    group.NodeId,
                    guards.Select(guard => guard.Predicate.Left).ToArray(),
                    consequents[0]),
            _ => null,
        };
    }

    private static RuleDisplayPredicate? OptionalGuard(RuleDisplayNode node) =>
        node is RuleDisplayPredicate
        {
            Predicate:
            {
                Operator: DomainPredicateOperator.IsNull,
                Left: { Kind: DomainOperandKind.Input, Reference: not null },
            },
        } predicate
            ? predicate
            : null;

    private static bool IsSupportedConsequence(RuleDisplayNode node) =>
        node is not RuleDisplayPredicate
        {
            Predicate.Operator: DomainPredicateOperator.IsNull or DomainPredicateOperator.IsNotNull,
        };

    private static bool ReferencesInput(RuleDisplayNode node, string reference) =>
        node switch
        {
            RuleDisplayPredicate predicate =>
                ReferencesInput(predicate.Predicate.Left, reference) ||
                predicate.Predicate.Right is { } right && ReferencesInput(right, reference),
            RuleDisplayGroup group => group.Children.Any(child => ReferencesInput(child, reference)),
            RuleDisplayConditional conditional =>
                conditional.Guards.Any(guard => ReferencesInput(guard, reference)) ||
                ReferencesInput(conditional.Consequent, reference),
            _ => false,
        };

    private static bool ReferencesInput(RuleOperand operand, string reference) =>
        operand switch
        {
            { Kind: DomainOperandKind.Input } =>
                string.Equals(operand.Reference, reference, StringComparison.Ordinal),
            { Kind: DomainOperandKind.Function } =>
                operand.Arguments.Any(argument => ReferencesInput(argument, reference)),
            _ => false,
        };
}
