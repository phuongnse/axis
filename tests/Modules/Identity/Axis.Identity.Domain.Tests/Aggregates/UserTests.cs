using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class UserTests
{
    private static Email ValidEmail => Email.Create("alice@acme.com").Value;

    [Fact]
    public void User_WhenCreated_ProducesActiveUnverifiedUser()
    {
        User user = User.Create("Alice Smith", ValidEmail);

        user.FullName.Should().Be("Alice Smith");
        user.Email.Should().Be(ValidEmail);
        user.Status.Should().Be(UserStatus.Active);
        user.IsEmailVerified.Should().BeFalse();
        user.LanguagePreference.Should().BeNull();
        user.ThemePreference.Should().BeNull();
    }

    [Fact]
    public void User_WhenCreatedWithSurroundingWhitespace_TrimsFullName()
    {
        User user = User.Create("  Alice Smith  ", ValidEmail);

        user.FullName.Should().Be("Alice Smith");
    }

    [Fact]
    public void User_WhenCreatedWithoutFullName_Throws()
    {
        Action act = () => User.Create("   ", ValidEmail);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Full name cannot be empty.*")
            .And.ParamName.Should().Be("fullName");
    }

    [Fact]
    public void VerifyEmail_WhenCalled_MarksUserVerified()
    {
        User user = User.Create("Alice Smith", ValidEmail);

        user.VerifyEmail();

        user.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public void SetLanguagePreference_WhenLanguageIsSupported_StoresPreference()
    {
        User user = User.Create("Alice Smith", ValidEmail);
        UserLanguage language = UserLanguage.Create("vi").Value;

        user.SetLanguagePreference(language);

        user.LanguagePreference.Should().Be(language);
    }

    [Fact]
    public void SetThemePreference_WhenThemeIsSupported_StoresPreference()
    {
        User user = User.Create("Alice Smith", ValidEmail);
        UserTheme theme = UserTheme.Create("dark").Value;

        user.SetThemePreference(theme);

        user.ThemePreference.Should().Be(theme);
    }
}
