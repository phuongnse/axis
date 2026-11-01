using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// While a computed field is pending, the record API and the process endpoints refuse a request that would read
/// its stale stored value: a sort on it, and a policy filter that reads it. Each test adds the computed field
/// <c>doubled</c> to the Request in release R1, which is computed inline, and changes its expression in release
/// R2, which activation leaves pending. After the worker runs the backfill, the same requests succeed.
/// </summary>
public sealed class PendingComputedRefusalTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Records = "/api/apps/StepApp/entities/Request/records";

    private const string Instances = "/api/apps/StepApp/processes/Decide/instances";

    private const string PolicyTitle = "Your access to this entity cannot be checked while a field is being recomputed.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_sorted_by_a_pending_field_is_409_with_an_error_at_sort_and_succeeds_after_the_backfill()
    {
        await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, "divisor * 2", "divisor * 3");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await SendAsync(maria, HttpMethod.Get, $"{Records}?sort=-doubled", TenantA);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = await ReadAsync(refused);
        Assert.Equal("A field is being recomputed.", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("doubled", problem.RootElement.GetProperty("errors").GetProperty("sort")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, HttpMethod.Get, Records, TenantA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, HttpMethod.Get, $"{Records}?sort=-divisor", TenantA)).StatusCode);

        await BackfillAsync(TenantA);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, HttpMethod.Get, $"{Records}?sort=-doubled", TenantA)).StatusCode);
    }

    [Fact]
    public async Task A_read_filter_on_a_pending_field_is_409_for_a_list_a_single_read_and_a_history_and_not_for_a_user_without_it()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, "divisor * 2", "divisor * 3", ReviewerReadsDoubled);
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        foreach (var path in new[] { Records, $"{Records}/{id:D}", $"{Records}/{Guid.NewGuid():D}", $"{Records}/{id:D}/history", $"{Records}?sort=nonsense" })
        {
            using var refused = await SendAsync(rita, HttpMethod.Get, path, TenantA);
            await AssertPolicyConflictAsync(refused);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, HttpMethod.Get, Records, TenantA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, HttpMethod.Get, $"{Records}/{id:D}", TenantA)).StatusCode);

        await BackfillAsync(TenantA);

        using var list = await GetAsync(rita, Records, TenantA);
        Assert.Contains(list.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
        using var single = await GetAsync(rita, $"{Records}/{id:D}", TenantA);
        Assert.Equal(21L, single.RootElement.GetProperty("values").GetProperty("doubled").GetInt64());
    }

    [Fact]
    public async Task An_update_filter_on_a_pending_field_is_409_and_writes_nothing_and_succeeds_after_the_backfill()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, "divisor * 2", "divisor * 3", FinanceUpdatesDoubled);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await PatchAsync(maria, id, version: 1, "Renamed");

        await AssertPolicyConflictAsync(refused);
        using (var stored = await GetAsync(maria, $"{Records}/{id:D}", TenantA))
        {
            Assert.Equal(1L, stored.RootElement.GetProperty("version").GetInt64());
            Assert.Equal(RequestTitle(id), stored.RootElement.GetProperty("values").GetProperty("title").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync(maria, Guid.NewGuid(), version: 1, "Renamed")).StatusCode);

        await BackfillAsync(TenantA);

        using var written = await PatchAsync(maria, id, version: 1, "Renamed");
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
    }

    [Fact]
    public async Task A_delete_filter_on_a_pending_field_is_409_and_deletes_nothing_and_succeeds_after_the_backfill()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, "divisor * 2", "divisor * 3", FinanceDeletesDoubled);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await SendAsync(maria, HttpMethod.Delete, $"{Records}/{id:D}", TenantA);

        await AssertPolicyConflictAsync(refused);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, HttpMethod.Get, $"{Records}/{id:D}", TenantA)).StatusCode);

        await BackfillAsync(TenantA);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(maria, HttpMethod.Delete, $"{Records}/{id:D}", TenantA)).StatusCode);
    }

    [Fact]
    public async Task A_process_start_and_an_instance_list_by_a_user_whose_read_filter_reads_a_pending_field_are_409_and_succeed_after_the_backfill()
    {
        var id = await fixture.InsertRequestAsync(TenantB, null, divisor: 7);
        await ActivateAsync(TenantB, "divisor * 2", "divisor * 3", ReviewerStartsAndInspectsDecide);
        using var rita = fixture.CreateClient().SignedInAs("rita");
        var before = await fixture.CountInstancesAsync(TenantB);

        using var start = await SendAsync(rita, HttpMethod.Post, Instances, TenantB, $$"""{ "subjectId": "{{id:D}}" }""");
        using var list = await SendAsync(rita, HttpMethod.Get, Instances, TenantB);

        await AssertPolicyConflictAsync(start);
        await AssertPolicyConflictAsync(list);
        Assert.Equal(before, await fixture.CountInstancesAsync(TenantB));

        await BackfillAsync(TenantB);

        using var started = await SendAsync(rita, HttpMethod.Post, Instances, TenantB, $$"""{ "subjectId": "{{id:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(rita, HttpMethod.Get, Instances, TenantB)).StatusCode);
    }

    // Activates R1 with the expression one, computed inline, then R2 with the expression two and no inline recompute,
    // so the entity is pending. The edit of another file applies to both.
    private async Task ActivateAsync(string tenant, string one, string two, Func<string, string, string?>? other = null)
    {
        await fixture.ActivateEditedAsync(tenant, Edit(one, other));
        await fixture.ActivateEditedAsync(tenant, Edit(two, other), inlineRecomputeLimit: 0);
        Assert.Equal(1L, await fixture.CountPendingComputedFieldsAsync(tenant));
    }

    private async Task BackfillAsync(string tenant)
    {
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        Assert.Equal(0L, await fixture.CountPendingComputedFieldsAsync(tenant));
    }

    private static Func<string, string, string?> Edit(string expression, Func<string, string, string?>? other) =>
        (path, content) => path == Path.Combine("entities", "request.json")
            ? content.Replace(
                "\"fields\": [",
                $"\"fields\": [\n    {{ \"name\": \"doubled\", \"type\": \"integer\", \"expression\": \"{expression}\" }},",
                StringComparison.Ordinal)
            : other?.Invoke(path, content) ?? content;

    private static string ReviewerReadsDoubled(string path, string content) =>
        path == Path.Combine("policies", "reviewer-access.json")
            ? content.Replace(
                """{ "name": "requestRead", "entity": "Request", "actions": ["read"], "fields": ["title", "department"] }""",
                """{ "name": "requestRead", "entity": "Request", "actions": ["read"], "filter": "doubled > 0" }""",
                StringComparison.Ordinal)
            : content;

    private static string ReviewerStartsAndInspectsDecide(string path, string content) =>
        path == Path.Combine("policies", "reviewer-access.json")
            ? ReviewerReadsDoubled(path, content).Replace(
                """{ "name": "requestRead", "entity": "Request", "actions": ["read"], "filter": "doubled > 0" }""",
                """{ "name": "requestRead", "entity": "Request", "actions": ["read"], "filter": "doubled > 0" }, { "process": "Decide", "actions": ["start", "inspect"] }""",
                StringComparison.Ordinal)
            : content;

    private static string FinanceUpdatesDoubled(string path, string content) =>
        path == Path.Combine("policies", "finance-access.json")
            ? content.Replace(
                "\"actions\": [\"update\"],",
                "\"actions\": [\"update\"], \"filter\": \"doubled > 0\",",
                StringComparison.Ordinal)
            : content;

    private static string FinanceDeletesDoubled(string path, string content) =>
        path == Path.Combine("policies", "finance-access.json")
            ? content.Replace(
                """{ "name": "requestRead", "entity": "Request", "actions": ["read", "create", "delete"] },""",
                """{ "name": "requestRead", "entity": "Request", "actions": ["read", "create"] }, { "entity": "Request", "actions": ["delete"], "filter": "doubled > 0" },""",
                StringComparison.Ordinal)
            : content;

    private static async Task AssertPolicyConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await ReadAsync(response);
        Assert.Equal(PolicyTitle, problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, long version, string title) =>
        SendAsync(client, HttpMethod.Patch, $"{Records}/{id:D}", TenantA, $$"""{ "version": {{version}}, "values": { "title": "{{title}}" } }""");

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string tenant, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, Host(tenant), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, string tenant)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path, tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
