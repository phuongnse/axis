using Axis.Server.Authentication;

namespace Axis.Server.Tests;

/// <summary>The callback accepts an ID token only when its <c>auth_time</c> is no older than the session's 8 hours, plus the clock skew.</summary>
public sealed class SignInRecencyTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _skew = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_auth_time_is_refused(string? authTime)
    {
        Assert.False(SignInRecency.IsRecent(authTime, _now, _skew));
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    [InlineData("9223372036854775807")]
    public void An_unreadable_auth_time_is_refused(string authTime)
    {
        Assert.False(SignInRecency.IsRecent(authTime, _now, _skew));
    }

    [Fact]
    public void A_login_one_minute_ago_is_accepted()
    {
        Assert.True(SignInRecency.IsRecent(Seconds(_now - TimeSpan.FromMinutes(1)), _now, _skew));
    }

    [Fact]
    public void A_login_exactly_eight_hours_ago_is_accepted()
    {
        Assert.True(SignInRecency.IsRecent(Seconds(_now - TimeSpan.FromHours(8)), _now, _skew));
    }

    [Fact]
    public void A_login_older_than_eight_hours_plus_the_skew_is_refused()
    {
        var limit = TimeSpan.FromHours(8) + _skew;

        Assert.True(SignInRecency.IsRecent(Seconds(_now - limit), _now, _skew));
        Assert.False(SignInRecency.IsRecent(Seconds(_now - limit - TimeSpan.FromSeconds(1)), _now, _skew));
        Assert.False(SignInRecency.IsRecent(Seconds(_now - TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1)), _now, TimeSpan.Zero));
    }

    private static string Seconds(DateTimeOffset time) => time.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
}
