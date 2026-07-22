using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Persistence;

public sealed class RulePersistenceJsonTests
{
    [Fact]
    public void Condition_WhenFunctionOperandIsRoundTripped_PreservesCapabilityAndArguments()
    {
        RulePredicateCondition condition = RulePredicateCondition.Create(
            "length-check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Function(
                RuleExpressionFunction.Length,
                [RuleOperand.Context("field.value").Value]).Value,
            RuleOperand.LiteralValue(
                RuleValue.Create(RuleValueType.Integer, ["5"]).Value).Value).Value;

        string json = RulePersistenceJson.SerializeCondition(condition);
        RulePredicateCondition restored = RulePersistenceJson.DeserializeCondition(json)
            .Should().BeOfType<RulePredicateCondition>().Subject;

        json.Should().Contain("\"function\":1").And.Contain("\"arguments\"");
        restored.Left.FunctionKind.Should().Be(RuleExpressionFunction.Length);
        restored.Left.Arguments.Should().ContainSingle(argument =>
            argument.Kind == RuleOperandKind.Context && argument.Reference == "field.value");
    }

    [Theory]
    [InlineData("""{"nodeId":"root","logicalOperator":0,"predicateOperator":0,"left":null,"right":null,"children":[]}""")]
    [InlineData("""{"nodeId":"root","logicalOperator":0,"predicateOperator":null,"left":{"kind":0,"reference":"field.value","literal":null},"right":null,"children":[]}""")]
    [InlineData("""{"nodeId":"root","logicalOperator":null,"predicateOperator":0,"left":{"kind":0,"reference":"field.value","literal":null},"right":null,"children":[{"nodeId":"child","logicalOperator":0,"predicateOperator":null,"left":null,"right":null,"children":[]}]}""")]
    public void DeserializeCondition_WhenNodeShapeIsAmbiguous_Throws(string json)
    {
        Action act = () => RulePersistenceJson.DeserializeCondition(json);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Persisted rule condition shape is invalid.");
    }
}
