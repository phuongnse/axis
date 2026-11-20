using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

public sealed class KeptExchangeTests
{
    private const int Limit = 64 * 1024;

    [Fact]
    public void A_text_within_the_limit_is_kept_whole()
    {
        var text = new string('a', Limit);

        var (kept, truncated) = KeptExchange.Cut(text);

        Assert.Equal(text, kept);
        Assert.False(truncated);
    }

    [Fact]
    public void A_text_over_the_limit_is_cut_to_the_limit()
    {
        var text = new string('a', Limit + 1);

        var (kept, truncated) = KeptExchange.Cut(text);

        Assert.Equal(Limit, Encoding.UTF8.GetByteCount(kept));
        Assert.StartsWith(kept, text, StringComparison.Ordinal);
        Assert.True(truncated);
    }

    [Theory]
    [InlineData("é", 1)]
    [InlineData("€", 2)]
    [InlineData("😀", 3)]
    public void A_text_is_cut_at_the_last_whole_character_within_the_limit(string character, int offset)
    {
        // The character starts offset bytes before the limit, so it does not fit and is dropped whole.
        var width = Encoding.UTF8.GetByteCount(character);
        var text = new string('a', Limit - width + offset) + character + "tail";

        var (kept, truncated) = KeptExchange.Cut(text);

        Assert.True(truncated);
        Assert.Equal(new string('a', Limit - width + offset), kept);
        Assert.True(Encoding.UTF8.GetByteCount(kept) <= Limit);
    }

    [Fact]
    public void A_request_shows_the_secret_headers_as_the_placeholder_and_cuts_its_body()
    {
        var connector = new ConnectorModel
        {
            Id = Guid.NewGuid(),
            Name = "Api",
            File = "connectors/api.json",
            BaseUrlBinding = "ApiUrl",
            Auth = new ConnectorAuthModel("header", "X-Api-Key", "ApiKey", "/auth"),
            Timeout = TimeSpan.FromSeconds(5),
            Operations = [],
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders");
        request.Headers.TryAddWithoutValidation("authorization", "Bearer abc");
        request.Headers.TryAddWithoutValidation("x-api-key", "key-value");
        request.Headers.TryAddWithoutValidation("Channel", "web");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var kept = KeptExchange.Request(request, "https://example.test/orders", [new("state", "open")], new string('b', Limit + 5), connector);

        Assert.Equal("POST", kept["method"]!.GetValue<string>());
        Assert.Equal("https://example.test/orders", kept["url"]!.GetValue<string>());
        Assert.Equal("open", kept["query"]!["state"]!.GetValue<string>());
        var headers = Assert.IsType<JsonObject>(kept["headers"]);
        Assert.Equal("[secret]", headers["Authorization"]!.GetValue<string>());
        Assert.Equal("[secret]", headers["x-api-key"]!.GetValue<string>());
        Assert.Equal("web", headers["Channel"]!.GetValue<string>());
        Assert.Contains("application/json", headers["Content-Type"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(Limit, kept["body"]!.GetValue<string>().Length);
        Assert.True(kept["truncated"]!.GetValue<bool>());
        Assert.DoesNotContain("abc", kept.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("key-value", kept.ToJsonString(), StringComparison.Ordinal);
    }
}
