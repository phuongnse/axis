using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// While a computed field is pending, a data source refuses a query that would decide from its stale stored
/// value: a sort, a filter, a group field, a measure or a search that reads it, and a read filter of the user that reads it.
/// A data source that only projects it shows the stored value. Each test adds a computed field in release R1,
/// which is computed inline, and changes its expression in release R2, which activation leaves pending.
/// After the worker runs the backfill, the same requests succeed.
/// </summary>
public sealed class PendingComputedDataSourceTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string PolicyTitle = "Your access to this entity cannot be checked while a field is being recomputed.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // On tenant a, whose Request has the computed field doubled.
    private static readonly IReadOnlyDictionary<string, string> _requestDataSources = new Dictionary<string, string>
    {
        ["data-sources/doubled-total.json"] = """
            {
              "id": "11816f44-aeea-4313-943c-08bc67c28700",
              "kind": "dataSource",
              "name": "DoubledTotal",
              "formatVersion": 1,
              "entity": "Request",
              "fields": [{ "name": "doubled", "path": "doubled" }],
              "aggregate": {
                "groupBy": [],
                "measures": [{ "name": "doubledSum", "function": "sum", "field": "doubled" }]
              }
            }
            """,
        ["data-sources/doubled-requests.json"] = """
            {
              "id": "8403b17d-4469-437e-b888-8b0947fd4b48",
              "kind": "dataSource",
              "name": "DoubledRequests",
              "formatVersion": 1,
              "entity": "Request",
              "fields": [
                { "name": "title", "path": "title" },
                { "name": "doubled", "path": "doubled" }
              ]
            }
            """,
        ["data-sources/doubled-count.json"] = """
            {
              "id": "5b0c2f1e-7a3d-4e8b-9c6a-1d2e3f4a5b6c",
              "kind": "dataSource",
              "name": "DoubledCount",
              "formatVersion": 1,
              "entity": "Request",
              "fields": [{ "name": "title", "path": "title" }],
              "aggregate": {
                "groupBy": [],
                "measures": [{ "name": "bigOnes", "function": "count", "filter": "doubled > 10" }]
              }
            }
            """,
    };

    // On tenant b, whose Department has the computed field tag.
    private static readonly IReadOnlyDictionary<string, string> _departmentDataSources = new Dictionary<string, string>
    {
        ["data-sources/department-tagged-requests.json"] = """
            {
              "id": "7da3bdcc-9132-4df8-af2d-2db72d6f2e21",
              "kind": "dataSource",
              "name": "DepartmentTaggedRequests",
              "formatVersion": 1,
              "entity": "Request",
              "fields": [{ "name": "title", "path": "title" }],
              "filter": "department.tag is not null"
            }
            """,
        ["data-sources/department-tag-search.json"] = """
            {
              "id": "2f6b8c1e-4a7d-4e59-9c3b-5d0a8e7f1b64",
              "kind": "dataSource",
              "name": "DepartmentTagSearch",
              "formatVersion": 1,
              "entity": "Request",
              "fields": [
                { "name": "title", "path": "title" },
                { "name": "departmentTag", "path": "department.tag" }
              ],
              "search": true
            }
            """,
    };

    [Fact]
    public async Task A_data_source_that_sums_a_pending_field_is_409_with_an_error_at_aggregate_and_succeeds_after_the_backfill()
    {
        await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, DoubledRequest("divisor * 2"), DoubledRequest("divisor * 3"));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await SendAsync(maria, "DoubledTotal", TenantA);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = await ReadAsync(refused);
        Assert.Equal("A field is being recomputed.", problem.RootElement.GetProperty("title").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["aggregate"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Contains("doubled", errors.GetProperty("aggregate")[0].GetString(), StringComparison.Ordinal);

        await BackfillAsync(TenantA);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DoubledTotal", TenantA)).StatusCode);
    }

    [Fact]
    public async Task A_measure_filter_that_reads_a_pending_field_is_409_with_an_error_at_aggregate_even_for_a_count_and_succeeds_after_the_backfill()
    {
        await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, DoubledRequest("divisor * 2"), DoubledRequest("divisor * 3"));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await SendAsync(maria, "DoubledCount", TenantA);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = await ReadAsync(refused);
        Assert.Equal("A field is being recomputed.", problem.RootElement.GetProperty("title").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["aggregate"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Contains("doubled", errors.GetProperty("aggregate")[0].GetString(), StringComparison.Ordinal);

        await BackfillAsync(TenantA);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DoubledCount", TenantA)).StatusCode);
    }

    [Fact]
    public async Task A_data_source_that_only_projects_a_pending_field_shows_its_stored_value_and_refuses_a_sort_on_it()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, DoubledRequest("divisor * 2"), DoubledRequest("divisor * 3"));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using (var rows = await ReadAsync(await SendAsync(maria, "DoubledRequests", TenantA)))
        {
            var item = Assert.Single(rows.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
            Assert.Equal(14L, item.GetProperty("values").GetProperty("doubled").GetInt64());
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DoubledRequests", TenantA, "?sort=title")).StatusCode);
        using var refused = await SendAsync(maria, "DoubledRequests", TenantA, "?sort=-doubled");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = await ReadAsync(refused);
        Assert.Contains("doubled", problem.RootElement.GetProperty("errors").GetProperty("sort")[0].GetString(), StringComparison.Ordinal);

        await BackfillAsync(TenantA);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DoubledRequests", TenantA, "?sort=-doubled")).StatusCode);
    }

    [Fact]
    public async Task A_data_source_filter_that_reads_a_pending_field_through_a_reference_is_409_with_an_error_at_filter_and_succeeds_after_the_backfill()
    {
        var department = await fixture.InsertDepartmentAsync(TenantB, "Lan");
        var request = await fixture.InsertRequestAsync(TenantB, department, divisor: 7);
        await ActivateAsync(TenantB, TaggedDepartment("manager"), TaggedDepartment("name"));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await SendAsync(maria, "DepartmentTaggedRequests", TenantB);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = await ReadAsync(refused);
        Assert.Equal("A field is being recomputed.", problem.RootElement.GetProperty("title").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["filter"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Contains("department.tag", errors.GetProperty("filter")[0].GetString(), StringComparison.Ordinal);

        await BackfillAsync(TenantB);

        using var rows = await ReadAsync(await SendAsync(maria, "DepartmentTaggedRequests", TenantB));
        Assert.Contains(rows.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == request);
    }

    [Fact]
    public async Task A_search_that_matches_a_pending_field_through_a_reference_is_409_with_an_error_at_search_and_succeeds_after_the_backfill()
    {
        var department = await fixture.InsertDepartmentAsync(TenantB, "Lan");
        var request = await fixture.InsertRequestAsync(TenantB, department, divisor: 7);
        await ActivateAsync(TenantB, TaggedDepartment("manager"), TaggedDepartment("name"));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var refused = await SendAsync(maria, "DepartmentTagSearch", TenantB, "?search=x");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = await ReadAsync(refused);
        Assert.Equal("A field is being recomputed.", problem.RootElement.GetProperty("title").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["search"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Contains("departmentTag", errors.GetProperty("search")[0].GetString(), StringComparison.Ordinal);

        // The same data source without a search only projects the stored value.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DepartmentTagSearch", TenantB)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DepartmentTagSearch", TenantB, "?search=")).StatusCode);

        await BackfillAsync(TenantB);

        using var rows = await ReadAsync(await SendAsync(maria, "DepartmentTagSearch", TenantB, "?search=x"));
        Assert.DoesNotContain(rows.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == request);
    }

    [Fact]
    public async Task A_user_whose_read_filter_reads_a_pending_field_gets_the_policy_409_with_no_errors_even_for_an_invalid_sort_and_another_user_gets_200()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        await ActivateAsync(TenantA, DoubledRequest("divisor * 2"), DoubledRequest("divisor * 3"));
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        foreach (var query in new[] { "", "?sort=nonsense" })
        {
            using var refused = await SendAsync(rita, "DoubledRequests", TenantA, query);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            using var problem = await ReadAsync(refused);
            Assert.Equal(PolicyTitle, problem.RootElement.GetProperty("title").GetString());
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(maria, "DoubledRequests", TenantA)).StatusCode);

        await BackfillAsync(TenantA);

        using var rows = await ReadAsync(await SendAsync(rita, "DoubledRequests", TenantA));
        Assert.Contains(rows.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
    }

    // Activates R1 with the edit one, computed inline, then R2 with the edit two and no inline recompute, so the
    // entity is pending. Both add the data sources and the grants to read them.
    private async Task ActivateAsync(string tenant, Func<string, string, string?> one, Func<string, string, string?> two)
    {
        var sources = tenant == TenantA ? _requestDataSources : _departmentDataSources;
        await fixture.ActivateEditedAsync(tenant, Grants(tenant, one), added: sources);
        await fixture.ActivateEditedAsync(tenant, Grants(tenant, two), inlineRecomputeLimit: 0, added: sources);
        Assert.Equal(1L, await fixture.CountPendingComputedFieldsAsync(tenant));
    }

    private async Task BackfillAsync(string tenant)
    {
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        Assert.Equal(0L, await fixture.CountPendingComputedFieldsAsync(tenant));
    }

    private static Func<string, string, string?> DoubledRequest(string expression) =>
        (path, content) => path == Path.Combine("entities", "request.json")
            ? content.Replace(
                "\"fields\": [",
                $"\"fields\": [\n    {{ \"name\": \"doubled\", \"type\": \"integer\", \"expression\": \"{expression}\" }},",
                StringComparison.Ordinal)
            : content;

    private static Func<string, string, string?> TaggedDepartment(string expression) =>
        (path, content) => path == Path.Combine("entities", "department.json")
            ? content.Replace(
                "\"fields\": [",
                $"\"fields\": [\n    {{ \"name\": \"tag\", \"type\": \"text\", \"searchable\": true, \"expression\": \"{expression}\" }},",
                StringComparison.Ordinal)
            : content;

    // Finance reads the data sources of the tenant. On tenant a, a reviewer reads requests through a read filter on
    // the computed field, and reads the data source DoubledRequests.
    private static Func<string, string, string?> Grants(string tenant, Func<string, string, string?> edit) =>
        (path, content) =>
        {
            content = edit(path, content) ?? content;
            var sources = tenant == TenantA
                ? """
                    { "name": "doubledTotalRead", "dataSource": "DoubledTotal", "actions": ["read"] },
                    { "name": "doubledRequestsRead", "dataSource": "DoubledRequests", "actions": ["read"] },
                    { "name": "doubledCountRead", "dataSource": "DoubledCount", "actions": ["read"] },
                    """
                : """
                    { "name": "departmentTaggedRequestsRead", "dataSource": "DepartmentTaggedRequests", "actions": ["read"] },
                    { "name": "departmentTagSearchRead", "dataSource": "DepartmentTagSearch", "actions": ["read"] },
                    """;
            return path switch
            {
                _ when path == Path.Combine("policies", "finance-access.json") => content.Replace(
                    """{ "name": "decideStart", "process": "Decide", "actions": ["start"] },""",
                    $$"""{{sources}} { "name": "decideStart", "process": "Decide", "actions": ["start"] },""",
                    StringComparison.Ordinal),
                _ when path == Path.Combine("policies", "reviewer-access.json") && tenant == TenantA => content.Replace(
                    """{ "name": "requestRead", "entity": "Request", "actions": ["read"], "fields": ["title", "department"] }""",
                    """
                    { "name": "requestRead", "entity": "Request", "actions": ["read"], "filter": "doubled > 0" },
                    { "name": "doubledRequestsRead2", "dataSource": "DoubledRequests", "actions": ["read"] }
                    """,
                    StringComparison.Ordinal),
                _ => content,
            };
        };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string dataSource, string tenant, string query = "")
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"/api/apps/StepApp/data-sources/{dataSource}/rows{query}", Host(tenant), null);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }
    }
}
