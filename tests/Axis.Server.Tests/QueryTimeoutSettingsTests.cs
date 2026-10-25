using Axis.Server.Http;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class QueryTimeoutSettingsTests
{
    [Fact]
    public void A_missing_value_takes_its_default()
    {
        var settings = Load();

        Assert.Equal(TimeSpan.FromSeconds(10), settings.For(QueryTimeoutSettings.Interactive));
        Assert.Equal(TimeSpan.FromMinutes(1), settings.For(QueryTimeoutSettings.Export));
    }

    [Fact]
    public void A_duration_is_read_for_each_setting()
    {
        var settings = Load((QueryTimeoutSettings.Interactive, "00:00:00.200"), (QueryTimeoutSettings.Export, "00:05:00"));

        Assert.Equal(TimeSpan.FromMilliseconds(200), settings.For(QueryTimeoutSettings.Interactive));
        Assert.Equal(TimeSpan.FromMinutes(5), settings.For(QueryTimeoutSettings.Export));
    }

    [Theory]
    [InlineData("Data:QueryTimeout", "0")]
    [InlineData("Data:QueryTimeout", "00:00:00")]
    [InlineData("Data:QueryTimeout", "-00:00:01")]
    [InlineData("Data:QueryTimeout", "00:00:00.0001")]
    [InlineData("Data:QueryTimeout", "abc")]
    [InlineData("Data:QueryTimeout", "30.00:00:00")]
    [InlineData("Data:ExportQueryTimeout", "0")]
    [InlineData("Data:ExportQueryTimeout", "abc")]
    public void A_value_that_is_not_a_positive_duration_stops_the_start(string setting, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load((setting, value)));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_setting_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Load().For("Data:Other"));
    }

    private static QueryTimeoutSettings Load(params (string Key, string Value)[] values) =>
        QueryTimeoutSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build());
}
