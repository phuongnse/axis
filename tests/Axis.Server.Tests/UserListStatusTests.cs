using Axis.Server.Users;

namespace Axis.Server.Tests;

/// <summary>The status of a user in the users list comes from the realm user's enabled flag and whether the user has a profile.</summary>
public sealed class UserListStatusTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void A_disabled_realm_user_is_disabled_whether_or_not_it_has_a_profile(bool enabled, bool hasProfile)
    {
        Assert.Equal("disabled", UserEndpoints.Status(enabled, hasProfile));
    }

    [Fact]
    public void An_enabled_user_without_a_profile_is_invited()
    {
        Assert.Equal("invited", UserEndpoints.Status(enabled: true, hasProfile: false));
    }

    [Fact]
    public void An_enabled_user_with_a_profile_is_active()
    {
        Assert.Equal("active", UserEndpoints.Status(enabled: true, hasProfile: true));
    }
}
