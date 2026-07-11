using Axis.BusinessObjects.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.BusinessObjects.Domain.Tests.ValueObjects;

public sealed class BusinessObjectChoiceOptionKeyTests
{
    [Fact]
    public void Create_WhenKeyIsValid_ReturnsKey()
    {
        BusinessObjectChoiceOptionKey.Create("approved").Value.Value.Should().Be("approved");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Approved")]
    [InlineData("not-valid")]
    public void Create_WhenKeyIsInvalid_ReturnsFailure(string key)
    {
        BusinessObjectChoiceOptionKey.Create(key).IsFailure.Should().BeTrue();
    }
}
