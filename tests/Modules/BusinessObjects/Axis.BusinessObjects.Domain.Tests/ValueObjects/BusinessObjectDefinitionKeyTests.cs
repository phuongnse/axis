using Axis.BusinessObjects.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.BusinessObjects.Domain.Tests.ValueObjects;

public sealed class BusinessObjectDefinitionKeyTests
{
    [Theory]
    [InlineData("customer")]
    [InlineData("customer_account_1")]
    [InlineData("a")]
    public void Create_WhenValueUsesSupportedFormat_ReturnsKey(string value)
    {
        BusinessObjectDefinitionKey.Create(value).Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Customer")]
    [InlineData(" customer")]
    [InlineData("_customer")]
    [InlineData("customer ")]
    [InlineData("customer-name")]
    [InlineData("customer account")]
    [InlineData("customer\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Create_WhenValueViolatesSupportedFormat_ReturnsFailure(string value)
    {
        BusinessObjectDefinitionKey.Create(value).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("Customer Account", "customer_account")]
    [InlineData("Don Hang 2026!", "don_hang_2026")]
    [InlineData("\u0110on hang", "don_hang")]
    [InlineData("123 Segment", "object_123_segment")]
    [InlineData("***", "object")]
    public void CreateFromName_WhenNameNeedsNormalization_DerivesSupportedKey(
        string name,
        string expectedKey)
    {
        BusinessObjectDefinitionKey.CreateFromName(name).Value.Value.Should().Be(expectedKey);
    }

    [Fact]
    public void CreateFromName_WhenNameIsMissing_ReturnsFailure()
    {
        BusinessObjectDefinitionKey.CreateFromName(" ").IsFailure.Should().BeTrue();
    }
}
