using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Processes.Instances;
using Axis.PurchasingStub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// The purchasing system stub's webhook receiver over real HTTP: it accepts a delivery signed with the webhook secret,
/// stores each webhook id once, refuses a bad signature or a stale timestamp with <c>401</c> and stores nothing, answers
/// <c>500</c> for a request number whose deliveries a test has set to fail, and accepts that number again once a test
/// clears the switch.
/// </summary>
public sealed class PurchasingStubWebhookTests : IAsyncLifetime
{
    private const string Route = "/webhooks/purchase-request-approved";
    private const string RequestNumber = "PR-2026-00042";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _app = PurchasingStubApp.Create(["--urls", "http://127.0.0.1:0"]);
        await _app.StartAsync(CancellationToken);
        var address = _app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_repeated_webhook_id_is_answered_twice_and_listed_once()
    {
        var id = $"{Guid.NewGuid():D}_{Guid.NewGuid():D}";
        using var first = await SendAsync(RequestNumber, id: id);
        using var second = await SendAsync(RequestNumber, id: id);

        Assert.True(first.IsSuccessStatusCode);
        Assert.True(second.IsSuccessStatusCode);
        var delivery = Assert.Single(await ListAsync());
        Assert.Equal(id, delivery.GetProperty("webhookId").GetString());
        Assert.Equal("PurchaseRequestApproved", delivery.GetProperty("type").GetString());
        Assert.Equal(RequestNumber, delivery.GetProperty("requestNumber").GetString());
    }

    [Theory]
    [InlineData("whsec_d3Jvbmctc2VjcmV0")]
    [InlineData("wrong-secret")]
    public async Task A_wrong_secret_is_refused_and_nothing_is_listed(string secret)
    {
        using var response = await SendAsync(RequestNumber, secret: secret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await ListAsync());
    }

    [Theory]
    [InlineData("webhook-id")]
    [InlineData("webhook-timestamp")]
    [InlineData("webhook-signature")]
    public async Task A_missing_header_is_refused_and_nothing_is_listed(string header)
    {
        using var response = await SendAsync(RequestNumber, omitHeaders: [header]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await ListAsync());
    }

    [Theory]
    [InlineData(-6 * 60L)]
    [InlineData(6 * 60L)]
    public async Task A_timestamp_six_minutes_off_is_refused_and_nothing_is_listed(long offsetSeconds)
    {
        using var response = await SendAsync(RequestNumber, timestamp: NowSeconds() + offsetSeconds);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task A_failing_request_number_is_answered_500_and_not_listed_while_another_is_listed()
    {
        using var fail = await _client.PostAsync($"/test/fail-webhook/{RequestNumber}", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, fail.StatusCode);

        using var failed = await SendAsync(RequestNumber);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Empty(await ListAsync());

        using var other = await SendAsync("PR-2026-00043");
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        var delivery = Assert.Single(await ListAsync());
        Assert.Equal("PR-2026-00043", delivery.GetProperty("requestNumber").GetString());
    }

    [Fact]
    public async Task A_request_number_accepted_again_is_answered_204_and_listed()
    {
        using var fail = await _client.PostAsync($"/test/fail-webhook/{RequestNumber}", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, fail.StatusCode);
        var id = $"{Guid.NewGuid():D}_{Guid.NewGuid():D}";
        using var failed = await SendAsync(RequestNumber, id: id);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Empty(await ListAsync());

        using var accept = await _client.PostAsync($"/test/accept-webhook/{RequestNumber}", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        using var accepted = await SendAsync(RequestNumber, id: id);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var delivery = Assert.Single(await ListAsync());
        Assert.Equal(id, delivery.GetProperty("webhookId").GetString());
    }

    [Fact]
    public async Task A_header_with_two_signatures_is_accepted_when_only_the_second_matches()
    {
        using var response = await SendAsync(RequestNumber, secret: "wrong-secret " + PurchasingStubApp.DefaultWebhookSecret);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await ListAsync());
    }

    private async Task<HttpResponseMessage> SendAsync(
        string number,
        string secret = PurchasingStubApp.DefaultWebhookSecret,
        string? id = null,
        long? timestamp = null,
        string[]? omitHeaders = null)
    {
        id ??= $"{Guid.NewGuid():D}_{Guid.NewGuid():D}";
        var seconds = timestamp ?? NowSeconds();
        var body = JsonSerializer.Serialize(new
        {
            type = "PurchaseRequestApproved",
            id = Guid.NewGuid(),
            timestamp = DateTimeOffset.UtcNow,
            data = new { entity = "PurchaseRequest", subjectId = Guid.NewGuid(), fields = new { number } },
        });

        var headers = new Dictionary<string, string>
        {
            ["webhook-id"] = id,
            ["webhook-timestamp"] = seconds.ToString(CultureInfo.InvariantCulture),
            ["webhook-signature"] = WebhookSignature.Sign(id, seconds, body, secret),
        };

        var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
        {
            if (omitHeaders is null || !omitHeaders.Contains(name))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return await _client.SendAsync(request, CancellationToken);
    }

    private async Task<List<JsonElement>> ListAsync()
    {
        var deliveries = await _client.GetFromJsonAsync<List<JsonElement>>(Route, CancellationToken);
        return deliveries!;
    }

    private static long NowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
