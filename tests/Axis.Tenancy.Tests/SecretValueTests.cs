using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Axis.Tenancy.Tests;

public sealed class SecretValueTests
{
    private const string Value = "s3cr3t-4f9b-x7q2";

    private readonly SecretValue _secret = new(Value);

    [Fact]
    public void Reveal_returns_the_value()
    {
        Assert.Equal(Value, _secret.Reveal());
    }

    [Fact]
    public void Text_shows_no_part_of_the_value()
    {
        AssertHidesValue(_secret.ToString());
        AssertHidesValue($"Token {_secret}");
        Assert.Equal("[secret]", _secret.ToString());
    }

    [Fact]
    public void Json_shows_no_part_of_the_value_alone_or_inside_an_object()
    {
        AssertHidesValue(JsonSerializer.Serialize(_secret));
        AssertHidesValue(JsonSerializer.Serialize(new { Token = _secret }));
        Assert.Equal("{\"Token\":\"[secret]\"}", JsonSerializer.Serialize(new { Token = _secret }));
    }

    [Fact]
    public void A_secret_is_never_read_from_json()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SecretValue>("\"x\""));
    }

    [Fact]
    public void Logging_shows_no_part_of_the_value_in_the_message_or_the_state()
    {
        var logger = new CapturingLogger();

        // A careless call, as code that logs a secret by mistake would make it.
#pragma warning disable CA1848, CA1873
        logger.LogInformation("Token {Token}", _secret);
#pragma warning restore CA1848, CA1873

        var entry = Assert.Single(logger.Entries);
        AssertHidesValue(entry.Message);
        Assert.NotEmpty(entry.State);
        Assert.All(entry.State, value => AssertHidesValue(Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    // Neither the value nor its first or last four characters may show.
    private static void AssertHidesValue(string? text)
    {
        Assert.NotNull(text);
        Assert.DoesNotContain(Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Value[..4], text, StringComparison.Ordinal);
        Assert.DoesNotContain(Value[^4..], text, StringComparison.Ordinal);
    }

    private sealed record Entry(string Message, IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class CapturingLogger : ILogger
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
    }
}
