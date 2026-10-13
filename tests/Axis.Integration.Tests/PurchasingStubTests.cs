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
/// The purchasing system stub over real HTTP: it honours the idempotency key, finds an order by
/// key, and loses or holds back a response on request after it has stored the order.
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

        using var found = await _client.GetAsync("/purchase-orders/by-key/key-1", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(number, await OrderNumberAsync(found));

        using var unknown = await _client.GetAsync("/purchase-orders/by-key/key-unknown", CancellationToken);
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

    [Theory]
    [InlineData(null, """{ "requestNumber": "PR-2026-00042", "amount": 120.5 }""")]
    [InlineData("key-1", """{ "amount": 120.5 }""")]
    [InlineData("key-1", """{ "requestNumber": "PR-2026-00042" }""")]
    public async Task A_create_without_the_key_the_request_number_or_the_amount_is_a_400(string? key, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/purchase-orders")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        using var response = await _client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListAsync());
    }

    private async Task<HttpResponseMessage> CreateAsync(string key, string requestNumber)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/purchase-orders")
        {
            Content = JsonContent.Create(new { requestNumber, supplierCode = "SUP-01", amount = 120.5m }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request, CancellationToken);
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
