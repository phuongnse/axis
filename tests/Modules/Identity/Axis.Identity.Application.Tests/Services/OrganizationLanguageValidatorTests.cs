using Axis.Identity.Application.Services;
using FluentAssertions;

namespace Axis.Identity.Application.Tests.Services;

public class OrganizationLanguageValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("en-US")]
    public void IsValid_WhenWellFormed_ReturnsTrue(string? language)
    {
        OrganizationLanguageValidator.IsValid(language).Should().BeTrue();
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("english")]
    [InlineData("en-us")]
    public void IsValid_WhenMalformed_ReturnsFalse(string language)
    {
        OrganizationLanguageValidator.IsValid(language).Should().BeFalse();
    }
}
