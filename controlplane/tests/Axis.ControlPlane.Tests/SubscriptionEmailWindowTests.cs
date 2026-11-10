using Axis.ControlPlane.Subscriptions;

namespace Axis.ControlPlane.Tests;

/// <summary>The window rule of the timeline emails, see <c>docs/reference/subscriptions.md#emails</c>.</summary>
public sealed class SubscriptionEmailWindowTests
{
    private static readonly DateTimeOffset _trialEnds = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _deletion = new(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-30, "trialStarted")]
    [InlineData(-8, "trialStarted")]
    [InlineData(-7, "trialEndsIn7Days")]
    [InlineData(-2, "trialEndsIn7Days")]
    [InlineData(-1, "trialEndsTomorrow")]
    [InlineData(-0.5, "trialEndsTomorrow")]
    [InlineData(0, null)]
    [InlineData(3, null)]
    public void A_trial_is_due_the_email_of_its_window_and_none_once_its_move_is_due(double daysFromTrialEnd, string? expected)
    {
        var now = _trialEnds.AddDays(daysFromTrialEnd);

        Assert.Equal(expected, SubscriptionClock.DueEmail("trialing", _trialEnds, _trialEnds, now));
    }

    [Theory]
    [InlineData(-30, "tenantReadOnly")]
    [InlineData(-8, "tenantReadOnly")]
    [InlineData(-7, "deletionIn7Days")]
    [InlineData(-0.5, "deletionIn7Days")]
    [InlineData(0, null)]
    [InlineData(2, null)]
    public void A_readOnly_subscription_is_due_the_email_of_its_window_and_none_once_its_move_is_due(double daysFromDeletion, string? expected)
    {
        var now = _deletion.AddDays(daysFromDeletion);

        Assert.Equal(expected, SubscriptionClock.DueEmail("readOnly", _trialEnds, _deletion, now));
    }

    [Fact]
    public void A_deleted_subscription_is_due_the_deletion_done_email_whenever_the_run_comes()
    {
        Assert.Equal("tenantDeleted", SubscriptionClock.DueEmail("deleted", _trialEnds, null, _deletion));
        Assert.Equal("tenantDeleted", SubscriptionClock.DueEmail("deleted", _trialEnds, null, _deletion.AddDays(100)));
    }

    [Fact]
    public void A_deleting_subscription_and_a_readOnly_one_with_no_date_are_due_no_email()
    {
        Assert.Null(SubscriptionClock.DueEmail("deleting", _trialEnds, _deletion, _deletion));
        Assert.Null(SubscriptionClock.DueEmail("readOnly", _trialEnds, null, _deletion));
    }

    [Fact]
    public void The_7_day_email_is_no_longer_due_once_the_1_day_window_starts()
    {
        Assert.Equal("trialEndsIn7Days", SubscriptionClock.DueEmail("trialing", _trialEnds, _trialEnds, _trialEnds.AddDays(-1).AddSeconds(-1)));
        Assert.Equal("trialEndsTomorrow", SubscriptionClock.DueEmail("trialing", _trialEnds, _trialEnds, _trialEnds.AddDays(-1)));
    }
}
