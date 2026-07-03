using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class UserLanguageTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    public void Create_WhenValueIsSupported_ReturnsLanguage(string value)
    {
        Result<UserLanguage> result = UserLanguage.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("EN")]
    public void Create_WhenValueIsUnsupported_ReturnsFailure(string value)
    {
        Result<UserLanguage> result = UserLanguage.Create(value);

        result.IsFailure.Should().BeTrue();
    }
}
