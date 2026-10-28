using System.Net;
using Axis.Server.Tests;
using Microsoft.Extensions.Logging;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A request that the server refuses with a <c>403</c> writes a warning with the HTTP method and the route template, and
/// the trace id and the tenant in its scope. The warning holds no value from the path. <c>manager</c> has no grant on
/// the RecordsApp entities and <c>rita</c> has a read grant.
/// </summary>
public sealed class RefusedRequestLogTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string RefusedText = "refused with 403";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_403_on_the_record_list_writes_a_warning_with_the_route_template_and_the_trace_id()
    {
        using var manager = fixture.CreateClient().SignedInAs("manager");
        var (traceId, header) = NewTraceparent();

        using var request = Request(HttpMethod.Get, Items, HostA);
        request.Headers.Add("traceparent", header);
        using var response = await manager.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entry = Assert.Single(Refusals(traceId));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("GET", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/apps/{app}/entities/{entity}/records", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Item", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordsApp", entry.Message, StringComparison.Ordinal);
        Assert.True(entry.Scope.ContainsKey("TenantId"));
    }

    [Fact]
    public async Task A_refused_write_without_the_csrf_header_writes_a_warning_with_the_route_template()
    {
        using var rita = fixture.CreateClient().SignedInAs("rita");
        var (traceId, header) = NewTraceparent();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Items, UriKind.Relative))
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = HostA;
        request.Headers.Add("traceparent", header);
        using var response = await rita.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entry = Assert.Single(Refusals(traceId));
        Assert.Contains("POST", entry.Message, StringComparison.Ordinal);
        Assert.Contains("{entity}", entry.Message, StringComparison.Ordinal);
        Assert.True(entry.Scope.ContainsKey("TenantId"));
    }

    [Fact]
    public async Task An_allowed_request_to_the_same_route_writes_no_refused_request_warning()
    {
        using var rita = fixture.CreateClient().SignedInAs("rita");
        var (traceId, header) = NewTraceparent();

        using var request = Request(HttpMethod.Get, Items, HostA);
        request.Headers.Add("traceparent", header);
        using var response = await rita.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Refusals(traceId));
    }

    private List<LogRecordEntry> Refusals(string traceId) =>
        [.. fixture.Logs.Records.Where(record =>
            record.Message.Contains(RefusedText, StringComparison.Ordinal)
            && record.Scope.TryGetValue("TraceId", out var scoped)
            && Equals(scoped?.ToString(), traceId))];

    private static (string TraceId, string Header) NewTraceparent()
    {
        var traceId = Guid.NewGuid().ToString("N");
        return (traceId, $"00-{traceId}-{Guid.NewGuid().ToString("N")[..16]}-01");
    }
}
