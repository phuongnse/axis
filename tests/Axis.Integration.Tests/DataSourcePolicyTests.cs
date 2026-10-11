using System.Globalization;
using System.Net;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Record and field policies on data sources. The rows, the count and the measures cover only root
/// records that match one of the user's read filters as well as the data source filter, and a data
/// source that shows a field the user may not read is refused as a whole. <c>fern</c> reads items
/// whose status is set and the tickets they requested, and has no grant on departments. <c>fia</c>
/// reads only the name and department of items, and nothing of departments. <c>faye</c> reads items
/// through a filter that overflows for an item with a quantity.
/// </summary>
public sealed class DataSourcePolicyTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string ForbiddenTitle = "You have no grant for this action.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rows_count_and_measures_cover_only_root_records_that_match_a_read_filter_and_the_data_source_filter()
    {
        await fixture.ResetAsync();
        var open = await InsertItemAsync("Desk", active: true, status: "open");
        var unset = await InsertItemAsync("Lamp", active: true, status: null);
        await InsertItemAsync("Chair", active: false, status: "closed");
        using var fern = fixture.CreateClient().SignedInAs("fern");

        // ActiveItems keeps active items with a quantity above 1. Only the open one has a status.
        using (var rows = await ReadJsonAsync(await SendAsync(fern, Path("ActiveItems"))))
        {
            Assert.Equal([open], RowIds(rows));
            Assert.Equal(1, rows.RootElement.GetProperty("totalCount").GetInt64());
        }

        using (var rows = await ReadJsonAsync(await SendAsync(fixture.Client, Path("ActiveItems"))))
        {
            Assert.Equal(new[] { open, unset }.Order(), RowIds(rows));
            Assert.Equal(2, rows.RootElement.GetProperty("totalCount").GetInt64());
        }

        // ItemTotals is one total row over the active items.
        using (var totals = await ReadJsonAsync(await SendAsync(fern, Path("ItemTotals"))))
        {
            var values = Assert.Single(totals.RootElement.GetProperty("items").EnumerateArray()).GetProperty("values");
            Assert.Equal(1, values.GetProperty("items").GetInt64());
            Assert.Equal(2, values.GetProperty("quantitySum").GetInt64());
        }

        using (var totals = await ReadJsonAsync(await SendAsync(fixture.Client, Path("ItemTotals"))))
        {
            var values = Assert.Single(totals.RootElement.GetProperty("items").EnumerateArray()).GetProperty("values");
            Assert.Equal(2, values.GetProperty("items").GetInt64());
            Assert.Equal(4, values.GetProperty("quantitySum").GetInt64());
        }
    }

    [Theory]
    [InlineData("fia", "ItemsByName")] // projects quantity and price
    [InlineData("fia", "ItemTotals")] // aggregates quantity and price
    [InlineData("fia", "ItemsByDepartment")] // department.name, with no grant on departments
    [InlineData("fern", "ItemsByDepartment")] // every item field, but no grant on departments
    public async Task A_data_source_that_shows_a_field_the_user_may_not_read_is_403(string user, string dataSource)
    {
        await fixture.ResetAsync();
        await InsertItemAsync("Desk", active: true, status: "open");
        using var client = fixture.CreateClient().SignedInAs(user);

        await AssertProblemAsync(await SendAsync(client, Path(dataSource)), HttpStatusCode.Forbidden, ForbiddenTitle);
    }

    [Fact]
    public async Task A_reference_label_shows_only_when_the_user_may_read_the_target_display_field()
    {
        await fixture.ResetAsync();
        var finance = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Finance" });
        await fixture.InsertAsync(
            TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["department"] = finance.ToString("D") });
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using (var rows = await ReadJsonAsync(await SendAsync(fia, Path("ItemNames"))))
        {
            var row = Assert.Single(rows.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(finance, row.GetProperty("values").GetProperty("department").GetGuid());
            Assert.Equal("{}", row.GetProperty("labels").GetRawText());
        }

        using (var rows = await ReadJsonAsync(await SendAsync(fixture.Client, Path("ItemNames"))))
        {
            var row = Assert.Single(rows.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("""{"department":"Finance"}""", row.GetProperty("labels").GetRawText());
        }
    }

    [Fact]
    public async Task The_combined_query_holds_only_parameters_and_a_user_id_with_sql_text_matches_no_row()
    {
        await fixture.ResetAsync();
        var model = fixture.Model;
        Assert.True(model.TryGetEntity("Ticket", out var ticket));
        Assert.True(model.TryGetEntity("Item", out var item));
        Assert.True(model.TryGetDataSource("OverdueTickets", out var overdue));
        Assert.True(model.TryGetDataSource("ItemsByDepartment", out var byDepartment));

        var tickets = DataSourceQueries.Statement(
            model, overdue, DataSourceSort.Default(overdue), Condition(model, ticket, SqlTextUser));
        var items = DataSourceQueries.Statement(
            model, byDepartment, DataSourceSort.Default(byDepartment), Condition(model, item, SqlTextUser));

        foreach (var sql in new[] { tickets.CountSql, tickets.PageSql })
        {
            Assert.Contains("@currentUser", sql, StringComparison.Ordinal);
            Assert.DoesNotContain(SqlTextUser, sql, StringComparison.Ordinal);
            Assert.DoesNotContain("1'='1", sql, StringComparison.Ordinal);
        }

        // The data source filter, its parameter and the read filters each name their own parameters.
        foreach (var sql in new[] { items.CountSql, items.PageSql })
        {
            Assert.Contains("@f0", sql, StringComparison.Ordinal);
            Assert.Contains("@p0", sql, StringComparison.Ordinal);
            Assert.Contains("@g0f0", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Archive", sql, StringComparison.Ordinal);
            Assert.DoesNotContain(SqlTextUser, sql, StringComparison.Ordinal);
        }

        // A day in the past, inserted directly, because the ticket validation refuses a past due time.
        var past = DateTimeOffset.UtcNow.AddDays(-1).ToString("O", CultureInfo.InvariantCulture);
        await fixture.InsertAsync(TenantA, "Ticket", new Dictionary<string, string?> { ["title"] = "Printer", ["dueAt"] = past, ["requester"] = "anna" });
        var ferns = await fixture.InsertAsync(
            TenantA, "Ticket", new Dictionary<string, string?> { ["title"] = "Shelf", ["dueAt"] = past, ["requester"] = "fern" });
        using var mallory = fixture.CreateClient().SignedInAs(SqlTextUser);
        using var fern = fixture.CreateClient().SignedInAs("fern");

        using (var rows = await ReadJsonAsync(await SendAsync(mallory, Path("OverdueTickets"))))
        {
            Assert.Empty(RowIds(rows));
            Assert.Equal(0, rows.RootElement.GetProperty("totalCount").GetInt64());
        }

        using (var rows = await ReadJsonAsync(await SendAsync(fern, Path("OverdueTickets"))))
        {
            Assert.Equal([ferns], RowIds(rows));
            Assert.Equal(1, rows.RootElement.GetProperty("totalCount").GetInt64());
        }
    }

    [Fact]
    public async Task A_read_filter_that_fails_makes_the_data_source_400_and_is_logged_by_policy_and_grant()
    {
        await fixture.ResetAsync();
        await InsertItemAsync("Desk", active: true, status: "open");
        using var faye = fixture.CreateClient().SignedInAs("faye");

        using (var response = await SendAsync(faye, Path("ActiveItems")))
        {
            using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            Assert.Equal("The data source filter could not be evaluated for these rows.", problem.RootElement.GetProperty("title").GetString());
            Assert.False(problem.RootElement.TryGetProperty("items", out _));
        }

        // The log names the policy and the grant, never a record value.
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Contains("A data source ActiveItems failed", StringComparison.Ordinal)
                && entry.Contains("grant 0 of policy FailingAccess", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains("FailingAccess", StringComparison.Ordinal) && entry.Contains("Desk", StringComparison.Ordinal));
    }

    /// <summary>The condition of the <c>read</c> filters of the <c>filtered</c> role on <paramref name="entity"/>.</summary>
    private static RecordCondition Condition(ApplicationModel model, EntityModel entity, string userId)
    {
        var filters = PolicyEvaluator.Access(model, ["filtered"], entity, PolicyAction.Read).Filters;
        Assert.NotNull(filters);
        return RecordFilters.Condition(model, entity, [.. filters.Select(filter => filter.Expression)], userId);
    }

    private Task<Guid> InsertItemAsync(string name, bool active, string? status) =>
        fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
        {
            ["name"] = name,
            ["quantity"] = "2",
            ["active"] = active ? "true" : "false",
            ["status"] = status,
        });

    private static string Path(string dataSource) => $"/api/apps/RecordsApp/data-sources/{dataSource}/rows";

    private static List<Guid> RowIds(JsonDocument page) =>
        [.. page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order()];

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        using (response)
        {
            using var problem = await ReadProblemAsync(response, status);
            Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = Request(HttpMethod.Get, path, HostA);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }
    }
}
