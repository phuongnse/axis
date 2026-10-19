using Axis.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

public sealed class TenancySettingsTests
{
    [Theory]
    [InlineData(null, TenancySource.Configuration)]
    [InlineData("", TenancySource.Configuration)]
    [InlineData("  ", TenancySource.Configuration)]
    [InlineData("configuration", TenancySource.Configuration)]
    [InlineData("directory", TenancySource.Directory)]
    public void The_source_is_read_from_the_setting_and_defaults_to_configuration(string? value, TenancySource expected)
    {
        Assert.Equal(expected, TenancySettings.Source(Settings(value)));
    }

    [Theory]
    [InlineData("other")]
    [InlineData("Directory")]
    [InlineData("CONFIGURATION")]
    public void Any_other_value_is_refused_naming_the_setting(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TenancySettings.Source(Settings(value)));

        Assert.Contains("Tenancy:Source", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{value}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData("", 30)]
    [InlineData("  ", 30)]
    [InlineData("00:00:05", 5)]
    [InlineData("00:01:00", 60)]
    public void The_refresh_interval_is_read_from_the_setting_and_defaults_to_30_seconds(string? value, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), TenancySettings.RefreshInterval(RefreshSettings(value)));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("abc")]
    public void A_refresh_interval_that_is_not_positive_is_refused_naming_the_setting(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TenancySettings.RefreshInterval(RefreshSettings(value)));

        Assert.Contains("Tenancy:RefreshInterval", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{value}'", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration RefreshSettings(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : [new KeyValuePair<string, string?>("Tenancy:RefreshInterval", value)])
            .Build();

    private static IConfiguration Settings(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : [new KeyValuePair<string, string?>("Tenancy:Source", value)])
            .Build();
}
