using Axis.Server.Http;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class RateLimitSettingsTests
{
    [Fact]
    public void Missing_settings_take_the_defaults()
    {
        var settings = Load();

        Assert.Equal(new RateLimitSettings(300, 1200, 1200, 60), settings);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.ReplenishmentPeriod);
    }

    [Fact]
    public void Each_setting_is_read_by_its_name()
    {
        var settings = Load(
            ("RateLimits:SignIn", "1"),
            ("RateLimits:Session", "2"),
            ("RateLimits:Bearer", "3"),
            ("RateLimits:Publishing", "4"));

        Assert.Equal(new RateLimitSettings(1, 2, 3, 4), settings);
        Assert.Equal(1, settings.For(Partition.SignIn));
        Assert.Equal(2, settings.For(Partition.Session));
        Assert.Equal(3, settings.For(Partition.Bearer));
        Assert.Equal(4, settings.For(Partition.Publishing));
    }

    [Theory]
    [InlineData("RateLimits:SignIn", "0")]
    [InlineData("RateLimits:Session", "-1")]
    [InlineData("RateLimits:Bearer", "abc")]
    [InlineData("RateLimits:Publishing", "")]
    [InlineData("RateLimits:Publishing", "1.5")]
    public void A_value_that_is_zero_or_less_or_not_a_number_fails_with_the_name_of_the_setting(string setting, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load((setting, value)));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    private static RateLimitSettings Load(params (string Key, string Value)[] values) =>
        RateLimitSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build());
}
