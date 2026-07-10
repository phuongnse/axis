using Axis.Rules.Domain;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleConditionEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenNestedConditionMatches_ReturnsDeterministicDiagnostics()
    {
        RuleContextSchema schema = Schema();
        RulePredicateCondition amount = Predicate(
            "amount-check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Context("record.amount").Value,
            RuleOperand.LiteralValue(Value(RuleValueType.Decimal, "1000")).Value);
        RulePredicateCondition status = Predicate(
            "status-check",
            RulePredicateOperator.Equal,
            RuleOperand.Context("record.status").Value,
            RuleOperand.LiteralValue(Value(RuleValueType.Text, "Open")).Value);
        RuleConditionGroup condition = RuleConditionGroup.Create(
            "root",
            RuleLogicalOperator.All,
            [amount, status]).Value;

        RuleConditionEvaluation result = RuleConditionEvaluator.Evaluate(
            condition,
            schema,
            new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                ["record.amount"] = Value(RuleValueType.Decimal, "1250"),
                ["record.status"] = Value(RuleValueType.Text, "Open"),
            }).Value;

        result.IsMatch.Should().BeTrue();
        result.Diagnostics.Select(diagnostic => diagnostic.NodeId)
            .Should().Equal("amount-check", "status-check", "root");
    }

    [Fact]
    public void Evaluate_WhenDateTimeOffsetsRepresentSameInstant_ReturnsEqual()
    {
        RulePredicateCondition condition = Predicate(
            "instant-check",
            RulePredicateOperator.Equal,
            RuleOperand.Context("record.occurred_at").Value,
            RuleOperand.LiteralValue(Value(RuleValueType.DateTime, "2026-07-10T03:30:00Z")).Value);

        RuleConditionEvaluator.Evaluate(
                condition,
                Schema(),
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["record.occurred_at"] = Value(RuleValueType.DateTime, "2026-07-10T10:30:00+07:00"),
                })
            .Value.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenConditionExceedsDepthLimit_ReturnsFailure()
    {
        RulePredicateCondition leaf = Predicate(
            "leaf",
            RulePredicateOperator.IsNotNull,
            RuleOperand.Context("record.status").Value);
        RuleConditionGroup inner = RuleConditionGroup.Create("inner", RuleLogicalOperator.Not, [leaf]).Value;
        RuleConditionGroup root = RuleConditionGroup.Create("root", RuleLogicalOperator.Not, [inner]).Value;

        RuleConditionEvaluator.Evaluate(
                root,
                Schema(),
                new Dictionary<string, RuleValue>(StringComparer.Ordinal),
                limits: new RuleEvaluationLimits(MaxDepth: 2))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenContextTypeDoesNotMatchSchema_ReturnsFailure()
    {
        RulePredicateCondition condition = Predicate(
            "type-check",
            RulePredicateOperator.IsNotNull,
            RuleOperand.Context("record.amount").Value);

        RuleConditionEvaluator.Evaluate(
                condition,
                Schema(),
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["record.amount"] = Value(RuleValueType.Text, "invalid"),
                })
            .IsFailure.Should().BeTrue();
    }

    private static RuleContextSchema Schema() => RuleContextSchema.Create(
        "objects.record",
        version: 1,
        RuleScope.Record,
        "Business object record",
        [
            RuleContextField.Create("record.amount", "Amount", RuleValueType.Decimal).Value,
            RuleContextField.Create("record.status", "Status", RuleValueType.Text).Value,
            RuleContextField.Create("record.occurred_at", "Occurred at", RuleValueType.DateTime).Value,
        ]).Value;

    private static RulePredicateCondition Predicate(
        string nodeId,
        RulePredicateOperator @operator,
        RuleOperand left,
        RuleOperand? right = null) =>
        RulePredicateCondition.Create(nodeId, @operator, left, right).Value;

    private static RuleValue Value(RuleValueType type, string value) =>
        RuleValue.Create(type, [value]).Value;
}
