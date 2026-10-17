using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// While a computed field is pending, a single read, a process step and a process start condition work it out
/// with the active release and store nothing, and a list still shows the stored value. Each test adds the
/// computed field <c>doubled</c> to the Request in release R1, which is computed inline, and changes its
/// expression in release R2, which activation leaves pending.
/// </summary>
public sealed class PendingComputedReadTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Records = "/api/apps/StepApp/entities/Request/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_single_read_of_a_pending_field_returns_the_active_releases_value_and_stores_nothing()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 7);
        var (r1, _) = await ActivateAsync(TenantA, "divisor * 2", "divisor * 3");
        Assert.Equal((14L, 0L), await fixture.DoubledAsync(TenantA, id));
        Assert.Equal(r1, await fixture.ComputedReleaseAsync(TenantA, id));
        Assert.Equal(1L, await fixture.CountPendingComputedFieldsAsync(TenantA));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var record = await GetAsync(maria, TenantA, id);

        var values = record.RootElement.GetProperty("values");
        Assert.Equal(21L, values.GetProperty("doubled").GetInt64());
        Assert.Equal(7L, values.GetProperty("divisor").GetInt64());
        Assert.Equal(1L, record.RootElement.GetProperty("version").GetInt64());
        Assert.Equal((14L, 0L), await fixture.DoubledAsync(TenantA, id));
        Assert.Equal(r1, await fixture.ComputedReleaseAsync(TenantA, id));
    }

    [Fact]
    public async Task A_list_of_a_pending_field_shows_the_stored_value_that_the_single_read_shows_recomputed()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: 8);
        await ActivateAsync(TenantA, "divisor * 4", "divisor * 5");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var single = await GetAsync(maria, TenantA, id);
        using var list = await GetJsonAsync(maria, $"{Records}?pageSize=100", TenantA);

        Assert.Equal(40L, single.RootElement.GetProperty("values").GetProperty("doubled").GetInt64());
        var listed = list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
        Assert.Equal(32L, listed.GetProperty("values").GetProperty("doubled").GetInt64());
    }

    [Fact]
    public async Task A_single_read_of_a_pending_field_whose_new_expression_fails_answers_200_with_null_and_records_no_error()
    {
        var id = await fixture.InsertRequestAsync(TenantA, null, divisor: long.MaxValue);
        await ActivateAsync(TenantA, "divisor - 1", "divisor + 1");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var record = await GetAsync(maria, TenantA, id);

        Assert.Equal(JsonValueKind.Null, record.RootElement.GetProperty("values").GetProperty("doubled").ValueKind);
        Assert.Equal((long.MaxValue - 1, 0L), await fixture.DoubledAsync(TenantA, id));
    }

    [Fact]
    public async Task A_decision_of_an_instance_pinned_to_the_older_release_takes_the_branch_of_the_newly_computed_value()
    {
        var id = await fixture.InsertRequestAsync(TenantB, null, divisor: 10);
        var (r1, _) = await ActivateAsync(TenantB, "divisor * 2", "divisor * 3", Divide);
        await fixture.DeleteBackfillItemsAsync(TenantB);
        var instance = await fixture.StartAsync(TenantB, "Divide", id, releaseId: r1);

        await fixture.RunUntilIdleAsync(TenantB, CancellationToken);

        var history = await fixture.HistoryAsync(TenantB, instance);
        Assert.Equal("0", history[0].Decision);
        Assert.Equal((20L, 0L), await fixture.DoubledAsync(TenantB, id));
    }

    [Fact]
    public async Task The_start_endpoint_checks_a_start_condition_on_a_pending_field_against_the_newly_computed_value()
    {
        var id = await fixture.InsertRequestAsync(TenantB, null, divisor: 5);
        await ActivateAsync(TenantB, "divisor * 4", "divisor * 6", StartCondition);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var request = RecordApiFixture.Request(
            HttpMethod.Post, "/api/apps/StepApp/processes/Decide/instances", Host(TenantB), $$"""{ "subjectId": "{{id:D}}" }""");
        using var response = await maria.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((20L, 0L), await fixture.DoubledAsync(TenantB, id));
    }

    // Activates R1 with the expression one, computed inline, then R2 with the expression two and no inline recompute,
    // so the entity is pending. The edit of another file applies to both.
    private async Task<(Guid R1, Guid R2)> ActivateAsync(string tenant, string one, string two, Func<string, string, string?>? other = null)
    {
        var r1 = await fixture.ActivateEditedAsync(tenant, Edit(one, other));
        var r2 = await fixture.ActivateEditedAsync(tenant, Edit(two, other), inlineRecomputeLimit: 0);
        return (r1, r2);
    }

    private static Func<string, string, string?> Edit(string expression, Func<string, string, string?>? other) =>
        (path, content) => path == Path.Combine("entities", "request.json")
            ? content.Replace(
                "\"fields\": [",
                $"\"fields\": [\n    {{ \"name\": \"doubled\", \"type\": \"integer\", \"expression\": \"{expression}\" }},",
                StringComparison.Ordinal)
            : other?.Invoke(path, content) ?? content;

    private static string Divide(string path, string content) =>
        path == Path.Combine("processes", "divide.json")
            ? content.Replace("round(amount / divisor, divisor) > 1", "doubled > 25", StringComparison.Ordinal)
            : content;

    private static string StartCondition(string path, string content) =>
        path == Path.Combine("processes", "decide.json")
            ? content.Replace(
                "\"start\": \"decide\",",
                "\"start\": \"decide\",\n  \"startCondition\": { \"expression\": \"doubled > 25\", \"message\": { \"textKey\": \"request.decide\" } },",
                StringComparison.Ordinal)
            : content;

    private static async Task<JsonDocument> GetAsync(HttpClient client, string tenant, Guid id) =>
        await GetJsonAsync(client, $"{Records}/{id:D}", tenant);

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, string tenant)
    {
        using var request = RecordApiFixture.Request(path, Host(tenant));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
