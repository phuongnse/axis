using Axis.Identity.Application.Commands.UpdateUserThemePreference;
using FluentAssertions;
using FluentValidation.Results;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class UpdateUserThemePreferenceCommandValidatorTests
{
    private readonly UpdateUserThemePreferenceCommandValidator _validator = new();

    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void Validate_WhenThemeIsSupported_AllowsCommand(string theme)
    {
        ValidationResult result = _validator.Validate(
            new UpdateUserThemePreferenceCommand(Guid.NewGuid(), theme));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("contrast")]
    public void Validate_WhenThemeIsUnsupported_RejectsCommand(string theme)
    {
        ValidationResult result = _validator.Validate(
            new UpdateUserThemePreferenceCommand(Guid.NewGuid(), theme));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Theme");
    }
}
