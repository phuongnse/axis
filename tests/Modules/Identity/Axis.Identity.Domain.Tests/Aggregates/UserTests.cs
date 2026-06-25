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
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.FullName.Should().Be("Alice Smith");
        user.Email.Should().Be(ValidEmail);
        user.Status.Should().Be(UserStatus.Active);
        user.IsEmailVerified.Should().BeFalse();
    }

    [Fact]
    public void VerifyEmail_WhenCalled_MarksUserVerified()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);

        user.VerifyEmail();

        user.IsEmailVerified.Should().BeTrue();
    }
}
