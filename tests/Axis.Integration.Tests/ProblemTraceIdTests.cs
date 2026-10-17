using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Server.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Every problem details answer carries the request's 32-hex W3C trace id as <c>traceId</c>, and the
/// log entries the request wrote carry the same id in their scope. <c>faye</c> reads items through a
/// filter that overflows for an item with a quantity.
/// </summary>
public sealed partial class ProblemTraceIdTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceIdPattern();

    [Fact]
    public async Task A_409_for_a_stale_version_carries_the_trace_id_of_its_request()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "2" });
        var (traceId, header) = NewTraceparent();

        using var request = Request(HttpMethod.Patch, $"{Items}/{id:D}", HostA, """{ "version": 9, "values": { "quantity": 5 } }""");
        request.Headers.Add("traceparent", header);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);

        AssertTraceId(problem, traceId);
    }

    [Fact]
    public async Task A_400_for_a_failing_read_filter_carries_the_trace_id_and_its_log_entry_has_the_same_trace_id()
    {
        await fixture.ResetAsync();
        var finance = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Finance" });
        await fixture.InsertAsync(
            TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "2", ["department"] = finance.ToString("D") });
        using var faye = fixture.CreateClient().SignedInAs("faye");
        var (traceId, header) = NewTraceparent();

        using var request = Request(HttpMethod.Get, Items, HostA);
        request.Headers.Add("traceparent", header);
        using var response = await faye.SendAsync(request, CancellationToken);
        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);

        AssertTraceId(problem, traceId);
        var entries = fixture.Logs.Records
            .Where(record => record.Message.StartsWith("A record list of entity Item failed", StringComparison.Ordinal)
                && record.Scope.TryGetValue("TraceId", out var scoped)
                && Equals(scoped?.ToString(), traceId))
            .ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.True(entry.Scope.ContainsKey("SpanId"));
            Assert.True(entry.Scope.ContainsKey("TenantId"));
        });
    }

    [Fact]
    public async Task A_404_for_an_unknown_host_carries_a_trace_id()
    {
        using var request = Request("/api/apps", "unknown.example.test");
        using var response = await fixture.CreateClient().SendAsync(request, CancellationToken);
        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);

        Assert.Matches(TraceIdPattern(), problem.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public void The_server_registers_the_OpenTelemetry_logger_provider()
    {
        var providers = fixture.Services.GetServices<ILoggerProvider>();

        Assert.Contains(providers, provider => provider is OpenTelemetryLoggerProvider);
    }

    private static (string TraceId, string Header) NewTraceparent()
    {
        var traceId = Guid.NewGuid().ToString("N");
        return (traceId, $"00-{traceId}-{Guid.NewGuid().ToString("N")[..16]}-01");
    }

    private static void AssertTraceId(JsonDocument problem, string traceId)
    {
        var value = problem.RootElement.GetProperty("traceId").GetString();
        Assert.Matches(TraceIdPattern(), value);
        Assert.Equal(traceId, value);
    }
}
