using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The form state endpoint on the <c>ItemSections</c> form of the RecordsApp fixture answers a
/// typical edit in the median of ten warm calls under 300 milliseconds.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class FormStatePerformanceTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string State = "/api/apps/RecordsApp/forms/ItemSections/state";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Median_of_ten_warm_calls_is_under_300_milliseconds()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "20" });
        var body = $$"""{ "recordId": "{{id:D}}", "values": { "quantity": 30, "price": "2.50" }, "touched": ["quantity", "price"] }""";
        for (var warmUp = 0; warmUp < 2; warmUp++)
        {
            using var warm = await PostAsync(fixture.Client, body);
        }

        var times = new List<TimeSpan>();
        for (var call = 0; call < 10; call++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await SendAsync(fixture.Client, body);
            watch.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            times.Add(watch.Elapsed);
        }

        var median = times.Order().ElementAt(5);
        Assert.True(median < TimeSpan.FromMilliseconds(300), $"The median was {median.TotalMilliseconds:F0} ms.");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string body)
    {
        using var request = Request(HttpMethod.Post, State, HostA, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, string body)
    {
        using var response = await SendAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
