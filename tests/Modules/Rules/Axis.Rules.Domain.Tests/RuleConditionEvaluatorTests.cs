using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleConditionEvaluatorTests
{
    [Fact]
    public void OutputContract_WhenFutureTypedShapeIsRequested_PreservesTypeAndCardinality()
    {
        RuleOutputContract output = RuleOutputContract.Create(
            RuleValueType.Text,
            RuleExpressionCardinality.Multiple).Value;

        output.Type.Should().Be(RuleValueType.Text);
        output.Cardinality.Should().Be(RuleExpressionCardinality.Multiple);
    }

    [Fact]
    public void DefinitionValidator_WhenOutputIsNotBoolean_RejectsCurrentConditionContract()
    {
        RuleConditionNode condition = Predicate(
            "value-present",
            RulePredicateOperator.IsNotNull,
            RuleOperand.Input("value").Value);
        RuleOutputContract output = RuleOutputContract.Create(
            RuleValueType.Text,
            RuleExpressionCardinality.Scalar).Value;

        Result result = RuleDefinitionValidator.Validate(
            [Input("value", RuleValueType.Text, true)],
            condition,
            output);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Rule conditions currently require a scalar Boolean output.");
    }

    [Fact]
    public void ExpressionLanguage_WhenRead_ExposesVersionedTypedCapabilities()
    {
        RuleExpressionLanguage.Version.Should().Be(1);
        RuleExpressionLanguage.Functions.Select(function => function.Function)
            .Should().BeEquivalentTo(Enum.GetValues<RuleExpressionFunction>(), options => options.WithStrictOrdering());
        RuleExpressionLanguage.Operators.Select(definition => definition.Operator)
            .Should().BeEquivalentTo(Enum.GetValues<RulePredicateOperator>(), options => options.WithStrictOrdering());
        RuleExpressionLanguage.OperandKinds.Select(definition => definition.Kind)
            .Should().BeEquivalentTo(Enum.GetValues<RuleOperandKind>(), options => options.WithStrictOrdering());
    }

    [Fact]
    public void Evaluate_WhenExpressionUsesRegisteredFunctions_ReturnsMatch()
    {
        RuleOperand length = RuleOperand.Function(
            RuleExpressionFunction.Length,
            [RuleOperand.Input("status").Value]).Value;
        RulePredicateCondition condition = Predicate(
            "length-check",
            RulePredicateOperator.GreaterThan,
            length,
            RuleOperand.LiteralValue(Value(RuleValueType.Integer, "3")).Value);

        RuleConditionEvaluator.Evaluate(
                condition,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["status"] = Value(RuleValueType.Text, "Active"),
                })
            .Value.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenFunctionArgumentsDoNotMatchCapabilitySignature_ReturnsFailure()
    {
        RuleOperand invalidLength = RuleOperand.Function(
            RuleExpressionFunction.Length,
            [RuleOperand.Input("amount").Value]).Value;
        RulePredicateCondition condition = Predicate(
            "length-check",
            RulePredicateOperator.GreaterThan,
            invalidLength,
            RuleOperand.LiteralValue(Value(RuleValueType.Integer, "3")).Value);

        RuleDefinitionValidator.Validate(
                [Input("amount", RuleValueType.Decimal, true)],
                condition,
                RuleOutputContract.BooleanMatch)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ValidateAndEvaluate_WhenFunctionCallsExceedLimit_ReturnFailure()
    {
        RuleOperand operand = RuleOperand.Input("status").Value;
        for (int index = 0; index < 3; index += 1)
            operand = RuleOperand.Function(RuleExpressionFunction.IsBlank, [operand]).Value;

        RulePredicateCondition condition = Predicate(
            "bounded-functions",
            RulePredicateOperator.Equal,
            operand,
            RuleOperand.LiteralValue(Value(RuleValueType.Boolean, "true")).Value);
        RuleEvaluationLimits limits = new(MaxFunctionCalls: 2);

        RuleDefinitionValidator.Validate(
                [Input("status", RuleValueType.Text, true)],
                condition,
                RuleOutputContract.BooleanMatch,
                limits)
            .IsFailure.Should().BeTrue();
        RuleConditionEvaluator.Evaluate(
                condition,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["status"] = Value(RuleValueType.Text, "Active"),
                },
                limits)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CreateGroup_WhenChildrenCollectionIsMutated_PreservesCondition()
    {
        RulePredicateCondition leaf = RulePredicateCondition.Create(
            "leaf",
            RulePredicateOperator.IsNull,
            RuleOperand.Input("value").Value).Value;
        RuleConditionGroup group = RuleConditionGroup.Create("root", RuleLogicalOperator.All, [leaf]).Value;

        Action mutate = () => ((IList<RuleConditionNode>)group.Children).Clear();

        mutate.Should().Throw<NotSupportedException>();
        group.Children.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_WhenNestedConditionMatches_ReturnsDeterministicDiagnostics()
    {
        RulePredicateCondition amount = Predicate(
            "amount-check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Input("amount").Value,
            RuleOperand.LiteralValue(Value(RuleValueType.Decimal, "1000")).Value);
        RulePredicateCondition status = Predicate(
            "status-check",
            RulePredicateOperator.Equal,
            RuleOperand.Input("status").Value,
            RuleOperand.LiteralValue(Value(RuleValueType.Text, "Open")).Value);
        RuleConditionGroup condition = RuleConditionGroup.Create("root", RuleLogicalOperator.All, [amount, status]).Value;

        RuleConditionEvaluation result = RuleConditionEvaluator.Evaluate(
            condition,
            new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                ["amount"] = Value(RuleValueType.Decimal, "1250"),
                ["status"] = Value(RuleValueType.Text, "Open"),
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
            RuleOperand.Input("occurred_at").Value,
            RuleOperand.LiteralValue(Value(RuleValueType.DateTime, "2026-07-10T03:30:00Z")).Value);

        RuleConditionEvaluator.Evaluate(
                condition,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal)
                {
                    ["occurred_at"] = Value(RuleValueType.DateTime, "2026-07-10T10:30:00+07:00"),
                })
            .Value.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenConditionExceedsDepthLimit_ReturnsFailure()
    {
        RulePredicateCondition leaf = Predicate(
            "leaf",
            RulePredicateOperator.IsNotNull,
            RuleOperand.Input("status").Value);
        RuleConditionGroup inner = RuleConditionGroup.Create("inner", RuleLogicalOperator.Not, [leaf]).Value;
        RuleConditionGroup root = RuleConditionGroup.Create("root", RuleLogicalOperator.Not, [inner]).Value;

        RuleConditionEvaluator.Evaluate(
                root,
                new Dictionary<string, RuleValue>(StringComparer.Ordinal),
                new RuleEvaluationLimits(MaxDepth: 2))
            .IsFailure.Should().BeTrue();
    }

    private static RuleInputDefinition Input(string key, RuleValueType type, bool required) =>
        RuleInputDefinition.Create(key, type, required).Value;

    private static RulePredicateCondition Predicate(
        string nodeId,
        RulePredicateOperator @operator,
        RuleOperand left,
        RuleOperand? right = null) =>
        RulePredicateCondition.Create(nodeId, @operator, left, right).Value;

    private static RuleValue Value(RuleValueType type, string value) =>
        RuleValue.Create(type, [value]).Value;
}
