using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class OrganizationSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("my-org-123")]
    [InlineData("a")]
    public void OrganizationSlug_WhenValueIsValid_IsCreatedSuccessfully(string value)
    {
        var result = OrganizationSlug.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    [InlineData("has.dot")]
    [InlineData("-starts-with-dash")]
    [InlineData("ends-with-dash-")]
    public void OrganizationSlug_WhenValueIsInvalid_ReturnsFailure(string value)
    {
        var result = OrganizationSlug.Create(value);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void OrganizationSlug_WhenExceedsMaxLength_ReturnsFailure()
    {
        var tooLong = new string('a', 64);

        var result = OrganizationSlug.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("63");
    }

    [Fact]
    public void OrganizationSlug_WhenValuesAreIdentical_AreEqual()
    {
        var a = OrganizationSlug.Create("acme-corp").Value;
        var b = OrganizationSlug.Create("acme-corp").Value;

        a.Should().Be(b);
    }
}
