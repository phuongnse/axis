using Axis.Rules.Infrastructure.Persistence;
using FluentAssertions;

namespace Axis.Rules.Infrastructure.Tests.Persistence;

public sealed class RulePersistenceJsonTests
{
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
