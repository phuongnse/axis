using Axis.Server.Files;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class FileSettingsTests
{
    [Fact]
    public void A_missing_value_takes_the_default_of_25_megabytes()
    {
        Assert.Equal(26_214_400, Load().MaxSize);
    }

    [Fact]
    public void A_whole_number_of_bytes_is_read()
    {
        Assert.Equal(1_048_576, Load("1048576").MaxSize);
        Assert.Equal(1, Load("1").MaxSize);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("10MB")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public void A_value_that_is_not_a_positive_whole_number_stops_the_start(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(value));

        Assert.Contains("Files:MaxSize", exception.Message, StringComparison.Ordinal);
    }

    private static FileSettings Load(string? value = null) =>
        FileSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : [new KeyValuePair<string, string?>("Files:MaxSize", value)])
            .Build());
}
