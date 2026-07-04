using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class UserThemeTests
{
    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void Create_WhenThemeIsSupported_ReturnsTheme(string theme)
    {
        Result<UserTheme> result = UserTheme.Create(theme);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(theme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contrast")]
    [InlineData("Dark")]
    public void Create_WhenThemeIsUnsupported_Fails(string theme)
    {
        Result<UserTheme> result = UserTheme.Create(theme);

        result.IsFailure.Should().BeTrue();
    }
}
