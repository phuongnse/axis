using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Persistence;

public sealed class RulePersistenceJsonTests
{
    [Fact]
    public void OutputContract_WhenRoundTripped_PreservesTypeAndCardinality()
    {
        string json = RulePersistenceJson.SerializeOutput(RuleOutputContract.BooleanMatch);
        RuleOutputContract restored = RulePersistenceJson.DeserializeOutput(json);

        restored.Should().Be(RuleOutputContract.BooleanMatch);
        json.Should().Contain("\"type\":5").And.Contain("\"cardinality\":0");
    }

    [Fact]
    public void InputMappings_WhenRoundTripped_PreservesContextAndLiteralMappings()
    {
        IReadOnlyDictionary<string, RuleInputMapping> mappings = new Dictionary<string, RuleInputMapping>
        {
            ["value"] = RuleInputMapping.FromContext("record.value").Value,
            ["threshold"] = RuleInputMapping.FromLiteral(["10", "20"]).Value,
        };

        Dictionary<string, RuleInputMapping> restored = RulePersistenceJson.DeserializeInputMappings(
            RulePersistenceJson.SerializeInputMappings(mappings));

        restored["value"].ContextKey.Should().Be("record.value");
        restored["threshold"].LiteralValues.Should().Equal("10", "20");
    }

    [Fact]
    public void InputMappings_WhenInsertionOrderDiffers_SerializesDeterministically()
    {
        Dictionary<string, RuleInputMapping> first = new()
        {
            ["value"] = RuleInputMapping.FromContext("record.value").Value,
            ["threshold"] = RuleInputMapping.FromLiteral(["10"]).Value,
        };
        Dictionary<string, RuleInputMapping> second = new()
        {
            ["threshold"] = RuleInputMapping.FromLiteral(["10"]).Value,
            ["value"] = RuleInputMapping.FromContext("record.value").Value,
        };

        RulePersistenceJson.SerializeInputMappings(first)
            .Should().Be(RulePersistenceJson.SerializeInputMappings(second));
    }

    [Fact]
    public void Condition_WhenFunctionOperandIsRoundTripped_PreservesCapabilityAndArguments()
    {
        RulePredicateCondition condition = RulePredicateCondition.Create(
            "length-check",
            RulePredicateOperator.GreaterThan,
            RuleOperand.Function(
                RuleExpressionFunction.Length,
                [RuleOperand.Input("field.value").Value]).Value,
            RuleOperand.LiteralValue(
                RuleValue.Create(RuleValueType.Integer, ["5"]).Value).Value).Value;

        string json = RulePersistenceJson.SerializeCondition(condition);
        RulePredicateCondition restored = RulePersistenceJson.DeserializeCondition(json)
            .Should().BeOfType<RulePredicateCondition>().Subject;

        json.Should().Contain("\"function\":1").And.Contain("\"arguments\"");
        restored.Left.FunctionKind.Should().Be(RuleExpressionFunction.Length);
        restored.Left.Arguments.Should().ContainSingle(argument =>
            argument.Kind == RuleOperandKind.Input && argument.Reference == "field.value");
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
