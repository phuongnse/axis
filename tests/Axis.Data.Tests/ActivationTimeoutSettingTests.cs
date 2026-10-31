using Microsoft.Extensions.Configuration;

namespace Axis.Data.Tests;

public sealed class ActivationTimeoutSettingTests
{
    [Fact]
    public void A_missing_value_takes_its_default_of_thirty_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ReleaseActivator.ReadActivationTimeout(Load(value: null)));
    }

    [Fact]
    public void A_duration_is_read_back()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ReleaseActivator.ReadActivationTimeout(Load("00:00:02")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("00:00:00.0001")]
    [InlineData("30.00:00:00")]
    public void A_value_that_is_not_a_positive_duration_stops_the_activation(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReleaseActivator.ReadActivationTimeout(Load(value)));

        Assert.Contains("Data:ActivationTimeout", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Load(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : [new KeyValuePair<string, string?>("Data:ActivationTimeout", value)])
            .Build();
}
