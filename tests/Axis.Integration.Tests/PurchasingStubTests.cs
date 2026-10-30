using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.PurchasingStub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// The purchasing system stub over real HTTP: it issues client credentials tokens and refuses an order call without a
/// valid one, honours the idempotency key, stores the order with its lines, finds an order by key, and loses or holds
/// back a response on request after it has stored the order.
/// </summary>
public sealed class PurchasingStubTests : IAsyncLifetime
{
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
    public async Task A_repeated_key_returns_the_same_order_and_stores_one()
    {
        using var first = await CreateAsync("key-1", "PR-2026-00042");
        using var second = await CreateAsync("key-1", "PR-2026-00042");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var number = await OrderNumberAsync(first);
        Assert.Matches(@"^PO-\d{4}-0001$", number);
        Assert.Equal(number, await OrderNumberAsync(second));

        var orders = await ListAsync();
        var order = Assert.Single(orders);
        Assert.Equal(number, order.GetProperty("orderNumber").GetString());
        Assert.Equal("key-1", order.GetProperty("idempotencyKey").GetString());
        Assert.Equal("PR-2026-00042", order.GetProperty("requestNumber").GetString());

        using var found = await LookupAsync("key-1");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(number, await OrderNumberAsync(found));

        using var unknown = await LookupAsync("key-unknown");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Drop_next_stores_the_order_and_closes_the_connection_without_a_response()
    {
        using var drop = await _client.PostAsync("/test/drop-next", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateAsync("key-lost", "PR-2026-00042"));

        var order = Assert.Single(await ListAsync());
        Assert.Equal("key-lost", order.GetProperty("idempotencyKey").GetString());

        // The drop happens once, so the retry gets the stored order back.
        using var retry = await CreateAsync("key-lost", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(order.GetProperty("orderNumber").GetString(), await OrderNumberAsync(retry));
        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task Drop_by_key_loses_only_the_first_create_with_that_key()
    {
        using var drop = await _client.PostAsync("/test/drop/key-dropped", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);

        using var other = await CreateAsync("key-other", "PR-2026-00041");
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateAsync("key-dropped", "PR-2026-00042"));

        using var retry = await CreateAsync("key-dropped", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(["key-other", "key-dropped"], (await ListAsync()).Select(o => o.GetProperty("idempotencyKey").GetString()));
    }

    [Fact]
    public async Task Drop_by_request_number_loses_only_the_first_create_for_that_number()
    {
        using var drop = await _client.PostAsync("/test/drop-request/PR-2026-00042", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);

        using var other = await CreateAsync("key-other", "PR-2026-00041");
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateAsync("key-dropped", "PR-2026-00042"));

        using var retry = await CreateAsync("key-dropped", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var another = await CreateAsync("key-another", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.Created, another.StatusCode);
        Assert.Equal(["key-other", "key-dropped", "key-another"], (await ListAsync()).Select(o => o.GetProperty("idempotencyKey").GetString()));
    }

    [Fact]
    public async Task Held_create_stores_the_order_and_answers_after_the_release()
    {
        using var hold = await _client.PostAsync("/test/hold-request/PR-2026-00042", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, hold.StatusCode);

        var create = CreateAsync("key-held", "PR-2026-00042");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await ListAsync()).Count == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The held create did not store its order within 10 seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }

        var order = Assert.Single(await ListAsync());
        Assert.Equal("key-held", order.GetProperty("idempotencyKey").GetString());
        await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken);
        Assert.False(create.IsCompleted);

        using var release = await _client.PostAsync("/test/release-request/PR-2026-00042", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, release.StatusCode);

        using var created = await create;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(order.GetProperty("orderNumber").GetString(), await OrderNumberAsync(created));

        // The hold happens once, so the next create for the number answers at once.
        using var repeat = await CreateAsync("key-held", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
    }

    [Fact]
    public async Task An_unavailable_request_answers_503_and_stores_no_order_until_it_is_available_again()
    {
        using var on = await _client.PostAsync("/test/unavailable-request/PR-2026-00042", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);

        using var other = await CreateAsync("key-other", "PR-2026-00041");
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        // The switch stays on, so every create for the number answers 503.
        using var first = await CreateAsync("key-1", "PR-2026-00042");
        using var second = await CreateAsync("key-1", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(["PR-2026-00041"], (await ListAsync()).Select(o => o.GetProperty("requestNumber").GetString()));

        using var off = await _client.PostAsync("/test/available-request/PR-2026-00042", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        using var created = await CreateAsync("key-1", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(["PR-2026-00041", "PR-2026-00042"], (await ListAsync()).Select(o => o.GetProperty("requestNumber").GetString()));
    }

    [Fact]
    public async Task An_unknown_request_stores_one_order_answers_without_its_number_and_fails_every_lookup()
    {
        using var on = await _client.PostAsync("/test/unknown-request/PR-2026-00042", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);

        using var other = await CreateAsync("key-other", "PR-2026-00041");
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.NotNull(await OrderNumberAsync(other));

        // The first create stores the order and answers 2xx with a body that has no order number.
        using var first = await CreateAsync("key-1", "PR-2026-00042");
        Assert.True(first.IsSuccessStatusCode);
        using var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync(CancellationToken));
        Assert.False(document.RootElement.TryGetProperty("orderNumber", out _));
        Assert.Equal(["PR-2026-00041", "PR-2026-00042"], (await ListAsync()).Select(o => o.GetProperty("requestNumber").GetString()));

        // The switch stays on for lookups, and only the first create misfits.
        using var lookup = await LookupAsync("key-1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lookup.StatusCode);
        using var again = await CreateAsync("key-1", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.NotNull(await OrderNumberAsync(again));
        Assert.Equal(2, (await ListAsync()).Count);

        // A lookup for another request's key still finds its order.
        using var found = await LookupAsync("key-other");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    [Fact]
    public async Task The_token_route_issues_a_bearer_token_for_the_client_credentials()
    {
        using var response = await TokenRequestAsync(PurchasingStubApp.DefaultClientId, PurchasingStubApp.DefaultClientSecret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", document.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(120, document.RootElement.GetProperty("expires_in").GetInt32());
    }

    [Theory]
    [InlineData("axis-purchasing", "wrong-secret")]
    [InlineData("other-client", PurchasingStubApp.DefaultClientSecret)]
    [InlineData("", "")]
    public async Task The_token_route_answers_401_for_wrong_credentials(string clientId, string clientSecret)
    {
        using var response = await TokenRequestAsync(clientId, clientSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Basic", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("invalid_client", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_token_route_answers_400_for_a_grant_type_other_than_client_credentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent([new("grant_type", "password")]),
        };

        using var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("unsupported_grant_type", document.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-token")]
    public async Task A_create_or_a_lookup_without_a_valid_token_is_a_401_and_stores_nothing(string? token)
    {
        using var created = await CreateAsync("key-1", "PR-2026-00042", token: token, withToken: false);
        using var lookup = await LookupAsync("key-1", token, withToken: false);

        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
        Assert.Contains("invalid_token", created.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, lookup.StatusCode);
        Assert.Empty(await ListAsync());

        // A stored order is not found by a lookup without a token either.
        using var stored = await CreateAsync("key-1", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.Created, stored.StatusCode);
        using var refused = await LookupAsync("key-1", token, withToken: false);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        await using var app = PurchasingStubApp.Create(["--urls", "http://127.0.0.1:0", "--Purchasing:TokenLifetime=1"]);
        await app.StartAsync(CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var issued = await client.SendAsync(TokenRequest(PurchasingStubApp.DefaultClientId, PurchasingStubApp.DefaultClientSecret), CancellationToken);
        using var document = JsonDocument.Parse(await issued.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("expires_in").GetInt32());
        var token = document.RootElement.GetProperty("access_token").GetString();
        using var before = new HttpRequestMessage(HttpMethod.Get, "/purchase-orders/by-key/key-1");
        before.Headers.Authorization = new("Bearer", token);
        using var accepted = await client.SendAsync(before, CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken);

        using var after = new HttpRequestMessage(HttpMethod.Get, "/purchase-orders/by-key/key-1");
        after.Headers.Authorization = new("Bearer", token);
        using var refused = await client.SendAsync(after, CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_created_order_lists_its_lines_and_the_client_of_its_token()
    {
        using var created = await CreateAsync("key-1", "PR-2026-00042");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var order = Assert.Single(await ListAsync());
        Assert.Equal(PurchasingStubApp.DefaultClientId, order.GetProperty("clientId").GetString());
        Assert.Equal(
            [("Desk", 2, 60.25m, 120.5m), ("Chair", 1, 80m, 80m)],
            order.GetProperty("lines").EnumerateArray().Select(line => (
                line.GetProperty("description").GetString()!,
                line.GetProperty("quantity").GetInt32(),
                line.GetProperty("unitPrice").GetDecimal(),
                line.GetProperty("amount").GetDecimal())));
    }

    [Theory]
    [InlineData(null, """{ "requestNumber": "PR-2026-00042", "amount": 120.5, "lines": [{ "description": "Desk", "quantity": 2, "unitPrice": 60.25, "amount": 120.5 }] }""")]
    [InlineData("key-1", """{ "amount": 120.5, "lines": [{ "description": "Desk", "quantity": 2, "unitPrice": 60.25, "amount": 120.5 }] }""")]
    [InlineData("key-1", """{ "requestNumber": "PR-2026-00042", "lines": [{ "description": "Desk", "quantity": 2, "unitPrice": 60.25, "amount": 120.5 }] }""")]
    [InlineData("key-1", """{ "requestNumber": "PR-2026-00042", "amount": 120.5 }""")]
    [InlineData("key-1", """{ "requestNumber": "PR-2026-00042", "amount": 120.5, "lines": [] }""")]
    [InlineData("key-1", """{ "requestNumber": "PR-2026-00042", "amount": 120.5, "lines": [{ "description": "Desk", "quantity": 2, "unitPrice": 60.25 }] }""")]
    [InlineData("key-1", """{ "requestNumber": "PR-2026-00042", "amount": 120.5, "lines": [{ "quantity": 2, "unitPrice": 60.25, "amount": 120.5 }] }""")]
    public async Task A_create_without_the_key_the_request_number_the_amount_or_the_lines_is_a_400(string? key, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/purchase-orders")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", await TokenAsync());
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        using var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListAsync());
    }

    private static HttpRequestMessage TokenRequest(string clientId, string clientSecret)
    {
        // The worker form-encodes both parts before it encodes the pair.
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{WebUtility.UrlEncode(clientId)}:{WebUtility.UrlEncode(clientSecret)}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {credentials}");
        return request;
    }

    private async Task<HttpResponseMessage> TokenRequestAsync(string clientId, string clientSecret)
    {
        using var request = TokenRequest(clientId, clientSecret);
        return await _client.SendAsync(request, CancellationToken);
    }

    /// <summary>A token that the stub issues for the default client credentials.</summary>
    private async Task<string> TokenAsync()
    {
        using var response = await TokenRequestAsync(PurchasingStubApp.DefaultClientId, PurchasingStubApp.DefaultClientSecret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// Creates an order with two lines. It signs in first and sends the issued token, unless <paramref name="withToken"/> is
    /// <see langword="false"/>, which sends <paramref name="token"/> if there is one.
    /// </summary>
    private async Task<HttpResponseMessage> CreateAsync(string key, string requestNumber, string? token = null, bool withToken = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/purchase-orders")
        {
            Content = JsonContent.Create(new
            {
                requestNumber,
                supplierCode = "SUP-01",
                amount = 200.5m,
                lines = new[]
                {
                    new { description = "Desk", quantity = 2, unitPrice = 60.25m, amount = 120.5m },
                    new { description = "Chair", quantity = 1, unitPrice = 80m, amount = 80m },
                },
            }),
        };
        request.Headers.Add("Idempotency-Key", key);
        SetToken(request, withToken ? await TokenAsync() : token);
        return await _client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> LookupAsync(string key, string? token = null, bool withToken = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/purchase-orders/by-key/{key}");
        SetToken(request, withToken ? await TokenAsync() : token);
        return await _client.SendAsync(request, CancellationToken);
    }

    private static void SetToken(HttpRequestMessage request, string? token)
    {
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }
    }

    private async Task<List<JsonElement>> ListAsync()
    {
        var orders = await _client.GetFromJsonAsync<List<JsonElement>>("/purchase-orders", CancellationToken);
        return orders!;
    }

    private static async Task<string?> OrderNumberAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return document.RootElement.GetProperty("orderNumber").GetString();
    }
}
