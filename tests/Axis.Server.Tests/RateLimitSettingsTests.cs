using System.Text.RegularExpressions;
using Axis.Server.Http;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed partial class RateLimitSettingsTests
{
    [Fact]
    public void Missing_settings_take_the_defaults()
    {
        var settings = Load();

        Assert.Equal(new RateLimitSettings(300, 1200, 1200, 60, 600), settings);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.ReplenishmentPeriod);
    }

    [Fact]
    public void Each_setting_is_read_by_its_name()
    {
        var settings = Load(
            ("RateLimits:SignIn", "1"),
            ("RateLimits:Session", "2"),
            ("RateLimits:Bearer", "3"),
            ("RateLimits:Publishing", "4"),
            ("RateLimits:Mcp", "5"));

        Assert.Equal(new RateLimitSettings(1, 2, 3, 4, 5), settings);
        Assert.Equal(1, settings.For(Partition.SignIn));
        Assert.Equal(2, settings.For(Partition.Session));
        Assert.Equal(3, settings.For(Partition.Bearer));
        Assert.Equal(4, settings.For(Partition.Publishing));
        Assert.Equal(5, settings.For(Partition.Mcp));
    }

    [Theory]
    [InlineData("RateLimits:SignIn", "0")]
    [InlineData("RateLimits:Session", "-1")]
    [InlineData("RateLimits:Bearer", "abc")]
    [InlineData("RateLimits:Mcp", "0")]
    [InlineData("RateLimits:Publishing", "")]
    [InlineData("RateLimits:Publishing", "1.5")]
    public void A_value_that_is_zero_or_less_or_not_a_number_fails_with_the_name_of_the_setting(string setting, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load((setting, value)));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rate_limit_table_of_the_host_settings_lists_the_settings_and_defaults_of_the_code()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "host-settings.md"));
        var section = lines.SkipWhile(line => line != "## Rate limit settings").Skip(1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));
        var documented = section
            .Select(line => SettingRow().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups[1].Value, int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        (string, int)[] coded =
        [
            (RateLimitSettings.SignInSetting, RateLimitSettings.DefaultSignIn),
            (RateLimitSettings.SessionSetting, RateLimitSettings.DefaultSession),
            (RateLimitSettings.BearerSetting, RateLimitSettings.DefaultBearer),
            (RateLimitSettings.PublishingSetting, RateLimitSettings.DefaultPublishing),
            (RateLimitSettings.McpSetting, RateLimitSettings.DefaultMcp),
        ];

        Assert.Equal(Enum.GetValues<Partition>().Length, coded.Length);
        Assert.Equal([.. coded.Order()], [.. documented.Order()]);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    [GeneratedRegex(@"^\|\s*`(RateLimits:\w+)`\s*\|\s*(\d+)\s*\|")]
    private static partial Regex SettingRow();

    private static RateLimitSettings Load(params (string Key, string Value)[] values) =>
        RateLimitSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build());
}
