using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class UserEmailVerificationTests
{
    private static Email ValidEmail => Email.Create("alice@example.com").Value;
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void New_user_is_not_email_verified()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.IsEmailVerified.Should().BeFalse();
    }

    [Fact]
    public void VerifyEmail_marks_user_as_verified()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);

        user.VerifyEmail();

        user.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public void VerifyEmail_already_verified_is_idempotent()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.VerifyEmail();

        var act = () => user.VerifyEmail();

        act.Should().NotThrow();
        user.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public void RecordFailedLogin_increments_counter()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.VerifyEmail();

        user.RecordFailedLogin();
        user.RecordFailedLogin();

        user.FailedLoginAttempts.Should().Be(2);
    }

    [Fact]
    public void RecordFailedLogin_locks_account_after_5_attempts()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.VerifyEmail();

        for (int i = 0; i < 5; i++)
            user.RecordFailedLogin();

        user.IsLockedOut.Should().BeTrue();
        user.LockedUntil.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ResetFailedLogins_clears_counter_and_lockout()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.VerifyEmail();
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();

        user.ResetFailedLogins();

        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_is_false_when_lockout_has_expired()
    {
        var user = User.Create("Alice", "Smith", ValidEmail, OrgId);
        user.VerifyEmail();
        // force lockout in the past
        for (int i = 0; i < 5; i++) user.RecordFailedLogin();
        user.SimulateLockoutExpiry();

        user.IsLockedOut.Should().BeFalse();
    }
}
