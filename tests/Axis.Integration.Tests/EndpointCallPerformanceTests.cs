using System.Diagnostics;
using System.Net;
using System.Text;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// Two hundred endpoint calls made one after another, each with its own <c>Idempotency-Key</c>,
/// all answer 201 within the time limit and leave a receipt each.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class EndpointCallPerformanceTests(EndpointCallFixture fixture) : IClassFixture<EndpointCallFixture>
{
    private const int Calls = 200;

    private const string Call = "/api/apps/EndpointApp/endpoints/ImportNote";

    // Each call authenticates the caller, then writes a record, an instance, a work item, audit
    // records and a receipt in one transaction, about 10 ms in all, so the run is expected to take
    // about 2 seconds. The bound is thirty times that, so shared CI runners do not make the test
    // flaky, and still low enough to catch a slowdown of an order of magnitude, such as a lost
    // index on the receipt table.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(1);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_hundred_calls_in_sequence_each_answer_201_within_the_time_limit_and_leave_a_receipt_each()
    {
        using var client = fixture.CreateClient();
        client.CallingAs("importer-system");

        // The first call starts the server's caches and its connections. It is not timed.
        using (var warmUp = await SendAsync(client, "warm-up"))
        {
            Assert.Equal(HttpStatusCode.Created, warmUp.StatusCode);
        }

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Calls; i++)
        {
            using var response = await SendAsync(client, $"key-{i}");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{Calls} endpoint calls took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The calls took {stopwatch.Elapsed}.");
        Assert.Equal(Calls + 1L, await fixture.CountAsync("SELECT count(*) FROM axis.endpoint_receipts"));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Call, UriKind.Relative))
        {
            Content = new StringContent($$"""{ "input": { "title": "Note {{key}}", "status": "open" } }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = EndpointCallFixture.Host;
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request, CancellationToken);
    }
}
