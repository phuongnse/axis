using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class UserEmailVerificationTests
{
    private static Email ValidEmail => Email.Create("alice@example.com").Value;

    [Fact]
    public void User_WhenCreated_IsNotEmailVerified()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.IsEmailVerified.Should().BeFalse();
    }

    [Fact]
    public void VerifyEmail_WhenCalled_MarksUserAsVerified()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.VerifyEmail();

        user.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public void VerifyEmail_WhenAlreadyVerified_IsIdempotent()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.VerifyEmail();

        Action act = () => user.VerifyEmail();

        act.Should().NotThrow();
        user.IsEmailVerified.Should().BeTrue();
    }
}
