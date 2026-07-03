using Axis.Identity.Application.Commands.UpdateUserLanguagePreference;
using FluentAssertions;
using FluentValidation.Results;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class UpdateUserLanguagePreferenceCommandValidatorTests
{
    private readonly UpdateUserLanguagePreferenceCommandValidator _validator = new();

    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    public void Validate_WhenLanguageIsSupported_AllowsCommand(string language)
    {
        ValidationResult result = _validator.Validate(
            new UpdateUserLanguagePreferenceCommand(Guid.NewGuid(), language));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("fr")]
    public void Validate_WhenLanguageIsUnsupported_RejectsCommand(string language)
    {
        ValidationResult result = _validator.Validate(
            new UpdateUserLanguagePreferenceCommand(Guid.NewGuid(), language));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Language");
    }
}
