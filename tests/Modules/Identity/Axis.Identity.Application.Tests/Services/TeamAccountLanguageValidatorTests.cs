using Axis.Identity.Application.Services;
using FluentAssertions;

namespace Axis.Identity.Application.Tests.Services;

public sealed class TeamAccountLanguageValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hant")]
    [InlineData("de-CH")]
    public void IsValid_WhenWellFormedTag_ReturnsTrue(string? tag)
    {
        TeamAccountLanguageValidator.IsValid(tag).Should().BeTrue();
    }

    [Theory]
    [InlineData("english")]
    [InlineData("en_US")]
    [InlineData("x")]
    public void IsValid_WhenMalformedTag_ReturnsFalse(string tag)
    {
        TeamAccountLanguageValidator.IsValid(tag).Should().BeFalse();
    }
}
