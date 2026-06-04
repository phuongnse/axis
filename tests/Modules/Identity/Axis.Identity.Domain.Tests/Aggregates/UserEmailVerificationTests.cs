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

    [Fact]
    public void RecordFailedLogin_WhenCalled_IncrementsCounter()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.VerifyEmail();

        user.RecordFailedLogin();
        user.RecordFailedLogin();

        user.FailedLoginAttempts.Should().Be(2);
    }

    [Fact]
    public void RecordFailedLogin_WhenFiveAttempts_LocksAccount()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.VerifyEmail();

        for (int i = 0; i < 5; i++)
            user.RecordFailedLogin();

        user.IsLockedOut.Should().BeTrue();
        user.LockedUntil.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ResetFailedLogins_WhenCalled_ClearsCounterAndLockout()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.VerifyEmail();
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();

        user.ResetFailedLogins();

        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_WhenLockoutHasExpired_ReturnsFalse()
    {
        User user = User.Create("Alice", "Smith", ValidEmail);
        user.VerifyEmail();
        // force lockout in the past
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();
        user.SimulateLockoutExpiry();

        user.IsLockedOut.Should().BeFalse();
    }
}
