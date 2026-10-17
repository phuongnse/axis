using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Record filters from policies on the record API. A grant with a filter reaches only the records
/// that match it, and the grants combine with OR. <c>fern</c> reads items through the filters
/// <c>false</c> and <c>status is not null</c>, writes items whose status is <c>open</c>, and reaches
/// the tickets they requested. <c>fred</c> reads items only through <c>false</c>, and <c>faye</c>
/// through a filter that overflows for an item with a quantity and one that matches items of the
/// Finance department.
/// </summary>
public sealed class RecordFilterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Tickets = "/api/apps/RecordsApp/entities/Ticket/records";
    private const string NotFoundTitle = "No record has this id.";
    private const string OutsideTitle = "The record is outside your grants for this action.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_holds_only_records_that_match_a_read_filter_and_any_other_record_is_404()
    {
        await fixture.ResetAsync();
        var open = await InsertItemAsync("Desk", "open");
        var closed = await InsertItemAsync("Chair", "closed");
        var unset = await InsertItemAsync("Lamp", null);
        using var fern = fixture.CreateClient().SignedInAs("fern");

        using (var page = await ReadJsonAsync(await SendAsync(fern, "GET", Items)))
        {
            Assert.Equal(new[] { open, closed }.Order(), Ids(page));
            Assert.Equal(2, page.RootElement.GetProperty("totalCount").GetInt64());
        }

        // A filter and a sort that cover the same records give the same count.
        using (var sorted = await ReadJsonAsync(await SendAsync(fern, "GET", $"{Items}?sort=-name&pageSize=1")))
        {
            Assert.Equal([open], Ids(sorted));
            Assert.Equal(2, sorted.RootElement.GetProperty("totalCount").GetInt64());
        }

        foreach (var readable in new[] { open, closed })
        {
            using var read = await SendAsync(fern, "GET", $"{Items}/{readable:D}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var history = await SendAsync(fern, "GET", $"{Items}/{readable:D}/history");
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        }

        await AssertProblemAsync(await SendAsync(fern, "GET", $"{Items}/{unset:D}"), HttpStatusCode.NotFound, NotFoundTitle);
        await AssertProblemAsync(await SendAsync(fern, "GET", $"{Items}/{unset:D}/history"), HttpStatusCode.NotFound, NotFoundTitle);

        // The record exists: a user with an unfiltered read grant reads it.
        using var anna = await SendAsync(fixture.Client, "GET", $"{Items}/{unset:D}");
        Assert.Equal(HttpStatusCode.OK, anna.StatusCode);
    }

    [Fact]
    public async Task A_false_filter_adds_no_records_to_another_grant_and_alone_reaches_none()
    {
        await fixture.ResetAsync();
        var open = await InsertItemAsync("Desk", "open");
        var closed = await InsertItemAsync("Chair", "closed");
        var unset = await InsertItemAsync("Lamp", null);
        using var fern = fixture.CreateClient().SignedInAs("fern");
        using var fred = fixture.CreateClient().SignedInAs("fred");

        using (var page = await ReadJsonAsync(await SendAsync(fern, "GET", Items)))
        {
            Assert.Equal(new[] { open, closed }.Order(), Ids(page));
        }

        using (var page = await ReadJsonAsync(await SendAsync(fred, "GET", Items)))
        {
            Assert.Empty(Ids(page));
            Assert.Equal(0, page.RootElement.GetProperty("totalCount").GetInt64());
        }

        foreach (var id in new[] { open, closed, unset })
        {
            await AssertProblemAsync(await SendAsync(fred, "GET", $"{Items}/{id:D}"), HttpStatusCode.NotFound, NotFoundTitle);
        }
    }

    [Fact]
    public async Task An_update_or_delete_of_a_record_outside_the_users_filters_changes_nothing()
    {
        await fixture.ResetAsync();
        var closed = await InsertItemAsync("Chair", "closed");
        var unset = await InsertItemAsync("Lamp", null);
        var open = await InsertItemAsync("Desk", "open");
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        using var fern = fixture.CreateClient().SignedInAs("fern");

        // The closed item is readable but outside the update and delete filters.
        await AssertProblemAsync(
            await SendAsync(fern, "PATCH", $"{Items}/{closed:D}", """{ "version": 1, "values": { "name": "Table" } }"""),
            HttpStatusCode.Forbidden,
            OutsideTitle);
        await AssertProblemAsync(await SendAsync(fern, "DELETE", $"{Items}/{closed:D}"), HttpStatusCode.Forbidden, OutsideTitle);

        // The item without a status is outside every read filter, so it does not exist for fern.
        await AssertProblemAsync(
            await SendAsync(fern, "PATCH", $"{Items}/{unset:D}", """{ "version": 1, "values": { "name": "Table" } }"""),
            HttpStatusCode.NotFound,
            NotFoundTitle);
        await AssertProblemAsync(await SendAsync(fern, "DELETE", $"{Items}/{unset:D}"), HttpStatusCode.NotFound, NotFoundTitle);

        Assert.Equal(3, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        foreach (var (id, name) in new[] { (closed, "Chair"), (unset, "Lamp") })
        {
            Assert.Empty(await fixture.AuditRecordsAsync(TenantA, id));
            using var stored = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}"));
            Assert.Equal(1, stored.RootElement.GetProperty("version").GetInt64());
            Assert.Equal(name, stored.RootElement.GetProperty("values").GetProperty("name").GetString());
        }

        // The open item matches every filter of fern, so it is updated and deleted.
        using (var update = await SendAsync(fern, "PATCH", $"{Items}/{open:D}", """{ "version": 1, "values": { "name": "Table" } }"""))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        using (var delete = await SendAsync(fern, "DELETE", $"{Items}/{open:D}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        Assert.Equal(2, (await fixture.AuditRecordsAsync(TenantA, open)).Count);
    }

    [Fact]
    public async Task An_update_that_moves_a_record_out_of_the_filter_or_a_create_outside_it_is_403_and_writes_nothing()
    {
        await fixture.ResetAsync();
        var open = await InsertItemAsync("Desk", "open");
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        using var fern = fixture.CreateClient().SignedInAs("fern");

        await AssertProblemAsync(
            await SendAsync(fern, "PATCH", $"{Items}/{open:D}", """{ "version": 1, "values": { "status": "closed" } }"""),
            HttpStatusCode.Forbidden,
            OutsideTitle);
        await AssertProblemAsync(
            await SendAsync(fern, "POST", Items, """{ "values": { "name": "Chair", "status": "closed" } }"""),
            HttpStatusCode.Forbidden,
            OutsideTitle);
        await AssertProblemAsync(
            await SendAsync(fern, "POST", Items, """{ "values": { "name": "Lamp" } }"""),
            HttpStatusCode.Forbidden,
            OutsideTitle);

        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        using (var stored = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{open:D}")))
        {
            Assert.Equal(1, stored.RootElement.GetProperty("version").GetInt64());
            Assert.Equal("open", stored.RootElement.GetProperty("values").GetProperty("status").GetString());
        }

        // A create inside the filter is written. A ticket's requester is its creator, so fern's
        // ticket matches the filter that names the current user.
        using (var create = await SendAsync(fern, "POST", Items, """{ "values": { "name": "Shelf", "status": "open" } }"""))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        using (var ticket = await SendAsync(fern, "POST", Tickets, """{ "values": { "title": "Broken shelf" } }"""))
        {
            Assert.Equal(HttpStatusCode.Created, ticket.StatusCode);
        }

        Assert.Equal(audits + 2, await fixture.AuditRecordCountAsync(TenantA));
    }

    [Fact]
    public async Task A_user_id_with_sql_text_is_a_parameter_and_matches_no_record()
    {
        await fixture.ResetAsync();
        var annas = await InsertTicketAsync("Printer", "anna");
        var ferns = await InsertTicketAsync("Shelf", "fern");
        using var mallory = fixture.CreateClient().SignedInAs(SqlTextUser);
        using var fern = fixture.CreateClient().SignedInAs("fern");

        using (var page = await ReadJsonAsync(await SendAsync(mallory, "GET", Tickets)))
        {
            Assert.Empty(Ids(page));
            Assert.Equal(0, page.RootElement.GetProperty("totalCount").GetInt64());
        }

        await AssertProblemAsync(await SendAsync(mallory, "GET", $"{Tickets}/{annas:D}"), HttpStatusCode.NotFound, NotFoundTitle);

        // The same filter reaches the tickets of a user whose id is plain text.
        using (var page = await ReadJsonAsync(await SendAsync(fern, "GET", Tickets)))
        {
            Assert.Equal([ferns], Ids(page));
        }
    }

    [Fact]
    public async Task A_filter_that_fails_denies_through_its_grant_only_and_fails_the_list()
    {
        await fixture.ResetAsync();
        var finance = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Finance" });
        var sales = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });
        var inFinance = await fixture.InsertAsync(
            TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "2", ["department"] = finance.ToString("D") });
        var inSales = await fixture.InsertAsync(
            TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Chair", ["quantity"] = "2", ["department"] = sales.ToString("D") });
        using var faye = fixture.CreateClient().SignedInAs("faye");

        using (var read = await SendAsync(faye, "GET", $"{Items}/{inFinance:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        await AssertProblemAsync(await SendAsync(faye, "GET", $"{Items}/{inSales:D}"), HttpStatusCode.NotFound, NotFoundTitle);
        await AssertProblemAsync(
            await SendAsync(faye, "GET", Items),
            HttpStatusCode.BadRequest,
            "A record filter could not be evaluated for these records.");

        // The log names the policy and the grant, never a record value.
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Contains("grant 0 of policy FailingAccess could not be evaluated for a record of entity Item", StringComparison.Ordinal));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Contains("A record list of entity Item failed", StringComparison.Ordinal)
                && entry.Contains("grant 1 of policy FailingAccess", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains("Desk", StringComparison.Ordinal) || entry.Contains("Chair", StringComparison.Ordinal));
    }

    private Task<Guid> InsertItemAsync(string name, string? status) =>
        fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = name, ["status"] = status });

    private Task<Guid> InsertTicketAsync(string title, string requester) =>
        fixture.InsertAsync(TenantA, "Ticket", new Dictionary<string, string?> { ["title"] = title, ["requester"] = requester });

    private static List<Guid> Ids(JsonDocument page) =>
        [.. page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order()];

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        using (response)
        {
            using var problem = await ReadProblemAsync(response, status);
            Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? body = null)
    {
        using var request = Request(new HttpMethod(method), path, HostA, body);
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
