using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The server and the worker trace a purchase request from its start request to its work. A work item
/// links to the span that queued it, history rows keep the trace id of their run, and no span holds a
/// field value, a secret, a body, a user id or a bound address. The same journey records the work item,
/// process instance and call attempt metrics.
/// </summary>
public sealed class TraceCorrelationTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private static readonly string[] _users = ["requester", "lan", "chau", "binh", "dung", "author"];

    // The attribute keys of each Axis span in the telemetry design.
    private static readonly Dictionary<string, string[]> _axisKeys = new(StringComparer.Ordinal)
    {
        ["axis.activation"] = ["axis.tenant", "axis.application"],
        ["axis.work_item.run"] = ["axis.tenant", "axis.work_item.kind", "axis.outcome"],
        ["axis.process.step"] = ["axis.tenant", "axis.application", "axis.process", "axis.step"],
        ["axis.connector.attempt"] = ["axis.tenant", "axis.application", "axis.connector", "axis.operation", "axis.outcome"],
        ["axis.connector.lookup"] = ["axis.tenant", "axis.application", "axis.connector", "axis.operation", "axis.outcome"],
        ["axis.package.call"] = ["axis.tenant", "axis.application", "axis.package", "axis.operation", "axis.outcome"],
    };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_start_request_and_the_workers_first_step_are_two_traces_and_the_step_links_to_the_request()
    {
        using var collector = new SpanCollector();
        var (requestTrace, _, _) = await StartTracedAsync();

        var run = Assert.Single(
            collector.Spans,
            span => span.OperationName == "axis.work_item.run"
                && span.Links.Any(link => link.Context.TraceId.ToHexString() == requestTrace));
        var runTrace = run.TraceId.ToHexString();
        Assert.NotEqual(requestTrace, runTrace);
        Assert.Equal("completed", run.GetTagItem("axis.outcome"));
        Assert.Contains(collector.Spans, span => span.OperationName == "axis.process.step" && span.TraceId.ToHexString() == runTrace);
    }

    [Fact]
    public async Task The_history_row_of_the_first_step_stores_the_trace_id_of_the_steps_run()
    {
        using var collector = new SpanCollector();
        var (requestTrace, instance, _) = await StartTracedAsync();

        var run = Assert.Single(
            collector.Spans,
            span => span.OperationName == "axis.work_item.run"
                && span.Links.Any(link => link.Context.TraceId.ToHexString() == requestTrace));
        var stored = await fixture.ScalarAsync(
            "SELECT trace_id FROM axis.process_step_history WHERE process_instance_id = @instance ORDER BY revision, started_at LIMIT 1",
            ("instance", instance));
        Assert.Equal(run.TraceId.ToHexString(), stored);
    }

    [Fact]
    public async Task No_span_of_the_purchase_request_journey_holds_a_field_value_a_secret_a_user_id_or_a_bound_address()
    {
        using var collector = new SpanCollector();
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var marker = $"Marker{Guid.NewGuid():N}";
        var subject = await CreateAsync(requester, marker, 10000, traceparent: null);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ApproveAsync(chau, instance, "managerApproval");
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ApproveAsync(dung, instance, "financeApproval");
        await fixture.RunUntilIdleAsync(CancellationToken);

        // The journey reached the connector call and the package call.
        Assert.Equal("completed", await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @instance", ("instance", instance)));
        var spans = collector.Spans;
        Assert.Contains(spans, span => span.OperationName == "axis.connector.attempt");
        Assert.Contains(spans, span => span.OperationName == "axis.package.call");
        Assert.Contains(spans, IsHttpClient);

        var stub = new Uri(fixture.PurchasingUrl);
        foreach (var span in spans)
        {
            foreach (var (key, value) in Values(span))
            {
                Assert.DoesNotContain(marker, value, StringComparison.Ordinal);
                Assert.DoesNotContain("sample-test-token", value, StringComparison.Ordinal);
                Assert.DoesNotContain(fixture.PurchasingUrl, value, StringComparison.Ordinal);
                Assert.DoesNotContain("purchasing.example.test", value, StringComparison.Ordinal);
                Assert.DoesNotContain(value, _users);
                if (span.Source.Name == "Axis" || IsHttpClient(span))
                {
                    // PostgreSQL and the stub's own incoming spans also use this address, so only these spans are checked.
                    Assert.False(value.Contains(stub.Host, StringComparison.Ordinal), $"{span.OperationName} holds the stub host in {key}.");
                    Assert.NotEqual(stub.Port.ToString(CultureInfo.InvariantCulture), value);
                }
            }

            if (_axisKeys.TryGetValue(span.OperationName, out var allowed))
            {
                Assert.Empty(span.TagObjects.Select(tag => tag.Key).Except([.. allowed, "error.type"], StringComparer.Ordinal));
            }
            else
            {
                Assert.NotEqual("Axis", span.Source.Name);
            }
        }
    }

    [Fact]
    public async Task The_purchase_request_journey_records_the_work_item_instance_and_call_attempt_metrics()
    {
        using var collector = new MetricCollector();
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateAsync(requester, $"Request {Guid.NewGuid():N}", 10000, traceparent: null);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ApproveAsync(chau, instance, "managerApproval");
        await fixture.RunUntilIdleAsync(CancellationToken);
        await ApproveAsync(dung, instance, "financeApproval");
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @instance", ("instance", instance)));
        Assert.True(collector.HasPoint("axis.process.instances.ended", ("axis.process", "PurchaseRequestApproval"), ("axis.outcome", "completed")));
        Assert.True(collector.HasPoint("axis.calls.attempts", ("axis.connector", "PurchasingSystem"), ("axis.outcome", "succeeded")));
        Assert.True(collector.HasPoint("axis.work_items.duration", ("axis.outcome", "completed")));
    }

    [Fact]
    public async Task Creating_a_record_emits_npgsql_spans_that_hold_the_statement_and_no_value()
    {
        using var collector = new SpanCollector();
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var marker = $"Marker{Guid.NewGuid():N}";
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        await CreateAsync(requester, marker, 500, $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        var database = collector.Spans.Where(span => span.TraceId.ToHexString() == traceId && span.Source.Name == "Npgsql").ToList();
        Assert.Contains(
            database,
            span => (span.GetTagItem("db.query.text") ?? span.GetTagItem("db.statement")) is string statement
                && statement.StartsWith("INSERT INTO", StringComparison.Ordinal));
        Assert.All(database, span => Assert.All(Values(span), tag => Assert.DoesNotContain(marker, tag.Value, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Creates a request, starts its process with a <c>traceparent</c> header, and runs the worker until idle.
    /// Returns the start request's trace id, the instance id and the subject id.
    /// </summary>
    private async Task<(string TraceId, Guid Instance, Guid Subject)> StartTracedAsync()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateAsync(requester, $"Request {Guid.NewGuid():N}", 500, traceparent: null);
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        using var request = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{App}/processes/PurchaseRequestApproval/instances",
            Host,
            $$"""{ "subjectId": "{{subject:D}}" }""");
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        using var response = await requester.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var instance = body.RootElement.GetProperty("id").GetGuid();
        await fixture.RunUntilIdleAsync(CancellationToken);
        return (traceId, instance, subject);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string title, decimal unitPrice, string? traceparent)
    {
        using var request = RecordApiFixture.Request(
            HttpMethod.Post,
            $"{App}/entities/PurchaseRequest/records",
            Host,
            $$"""
            {
              "values": {
                "title": "{{title}}",
                "status": "draft",
                "department": "{{Operations:D}}",
                "lineItems": [{ "description": "Desk", "quantity": 1, "unitPrice": {{unitPrice.ToString(CultureInfo.InvariantCulture)}} }]
              }
            }
            """);
        if (traceparent is not null)
        {
            request.Headers.Add("traceparent", traceparent);
        }

        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task ApproveAsync(HttpClient client, Guid instance, string step)
    {
        var task = (Guid)(await fixture.ScalarAsync(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @instance AND state = 'open' AND step = @step",
            ("instance", instance),
            ("step", step)))!;
        using var response = await CompleteAsync(client, task, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static bool IsHttpClient(Activity span) => span.Kind == ActivityKind.Client && span.GetTagItem("http.request.method") is not null;

    /// <summary>The key and text of each attribute of the span, including its name.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Values(Activity span)
    {
        yield return new("name", span.OperationName);
        yield return new("display name", span.DisplayName);
        foreach (var tag in span.TagObjects)
        {
            yield return new(tag.Key, Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? "");
        }
    }
}
