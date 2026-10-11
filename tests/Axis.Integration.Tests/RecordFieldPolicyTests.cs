using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Field policies on the record API. <c>fia</c> reads an item's <c>name</c> and
/// <c>department</c>, creates and updates its <c>name</c> and <c>quantity</c>, reads and creates a
/// ticket's <c>title</c>, and reads an order's <c>lineCount</c> but not its <c>title</c>, the
/// display field. They hold no grant on departments. <c>anna</c> holds every grant.
/// </summary>
public sealed class RecordFieldPolicyTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Tickets = "/api/apps/RecordsApp/entities/Ticket/records";
    private const string Orders = "/api/apps/RecordsApp/entities/Order/records";
    private const string UnwritableTitle = "You have no grant to write these fields.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_and_write_responses_hold_only_the_readable_fields()
    {
        await fixture.ResetAsync();
        var (id, departmentId) = await SeedItemAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using (var read = await ReadJsonAsync(await SendAsync(fia, "GET", $"{Items}/{id:D}", null)))
        {
            Assert.Equal(["id", "version", "values", "labels"], read.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal(
                [("name", "\"Desk\""), ("department", $"\"{departmentId:D}\"")],
                Values(read.RootElement));
        }

        using (var list = await ReadJsonAsync(await SendAsync(fia, "GET", Items, null)))
        {
            var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(["name", "department"], Values(item).Select(value => value.Name));
        }

        // The computed total is left out, though the body set quantity.
        using (var created = await SendAsync(fia, "POST", Items, """{ "values": { "name": "Chair", "quantity": 2 } }"""))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var body = await ParseAsync(created);
            Assert.Equal([("name", "\"Chair\""), ("department", "null")], Values(body.RootElement));
            Assert.Equal("{}", body.RootElement.GetProperty("labels").GetRawText());
        }

        using (var updated = await ReadJsonAsync(await SendAsync(fia, "PATCH", $"{Items}/{id:D}", """{ "version": 1, "values": { "name": "Table" } }""")))
        {
            Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
            Assert.Equal(
                [("name", "\"Table\""), ("department", $"\"{departmentId:D}\"")],
                Values(updated.RootElement));
        }

        // The sequence field number and the creator field requester are set, and left out.
        using (var ticket = await SendAsync(fia, "POST", Tickets, """{ "values": { "title": "Broken chair" } }"""))
        {
            Assert.Equal(HttpStatusCode.Created, ticket.StatusCode);
            using var body = await ParseAsync(ticket);
            Assert.Equal([("title", "\"Broken chair\"")], Values(body.RootElement));
            using var stored = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", ticket.Headers.Location!.OriginalString, null));
            Assert.NotEqual(JsonValueKind.Null, stored.RootElement.GetProperty("values").GetProperty("number").ValueKind);
            Assert.Equal("fia", stored.RootElement.GetProperty("values").GetProperty("requester").GetString());
        }

        // anna's grants cover every field.
        using var full = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}", null));
        Assert.Contains(("total", "37.50"), Values(full.RootElement));
    }

    [Theory]
    [InlineData("POST", """{ "values": { "name": "Chair", "price": 2, "status": "open" } }""")]
    [InlineData("PATCH", """{ "version": 1, "values": { "name": "Chair", "price": 2, "status": "open" } }""")]
    public async Task A_write_of_a_field_no_grant_covers_is_403_at_each_field_and_writes_nothing(string method, string body)
    {
        await fixture.ResetAsync();
        var (id, _) = await SeedItemAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        var path = method == "POST" ? Items : $"{Items}/{id:D}";

        using var response = await SendAsync(fia, method, path, body);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(UnwritableTitle, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            ["/values/price", "/values/status"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        using var stored = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}", null));
        Assert.Equal(1, stored.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("Desk", stored.RootElement.GetProperty("values").GetProperty("name").GetString());
    }

    [Fact]
    public async Task History_details_leave_out_the_fields_the_user_may_not_read()
    {
        await fixture.ResetAsync();
        var (id, _) = await SeedItemAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");
        using (var update = await SendAsync(fixture.Client, "PATCH", $"{Items}/{id:D}", """{ "version": 1, "values": { "name": "Table", "price": 9 } }"""))
        {
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        using var own = await ReadJsonAsync(await SendAsync(fia, "GET", $"{Items}/{id:D}/history", null));
        using var full = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}/history", null));

        Assert.Equal((2, """["name"]"""), Details(own));
        Assert.Equal((2, """["name","price"]"""), Details(full));
    }

    [Fact]
    public async Task A_reference_label_is_left_out_when_the_user_may_not_read_the_target_display_field()
    {
        await fixture.ResetAsync();
        var (id, departmentId) = await SeedItemAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using var own = await ReadJsonAsync(await SendAsync(fia, "GET", $"{Items}/{id:D}", null));
        using var full = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}", null));

        Assert.Equal(departmentId.ToString("D"), own.RootElement.GetProperty("values").GetProperty("department").GetString());
        Assert.Equal("{}", own.RootElement.GetProperty("labels").GetRawText());
        Assert.Equal("""{"department":"Sales"}""", full.RootElement.GetProperty("labels").GetRawText());
    }

    [Fact]
    public async Task A_list_sorted_by_a_field_the_user_may_not_read_is_400()
    {
        await fixture.ResetAsync();
        await SeedItemAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using (var hidden = await SendAsync(fia, "GET", $"{Items}?sort=-price", null))
        {
            using var problem = await ReadProblemAsync(hidden, HttpStatusCode.BadRequest);
            Assert.Equal(["sort"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        using var readable = await SendAsync(fia, "GET", $"{Items}?sort=-name", null);
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
    }

    [Fact]
    public async Task A_search_is_400_when_the_user_may_not_read_the_display_field()
    {
        await fixture.ResetAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");

        // fia reads an order's lineCount but not its title, the display field.
        using (var hidden = await SendAsync(fia, "GET", $"{Orders}?search=Desk", null))
        {
            using var problem = await ReadProblemAsync(hidden, HttpStatusCode.BadRequest);
            Assert.Equal(["search"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        using (var unsearched = await SendAsync(fia, "GET", Orders, null))
        {
            Assert.Equal(HttpStatusCode.OK, unsearched.StatusCode);
        }

        using var full = await SendAsync(fixture.Client, "GET", $"{Orders}?search=Desk", null);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
    }

    /// <summary>Inserts the department Sales and an item Desk in it, with quantity and price, and returns their ids.</summary>
    private async Task<(Guid Item, Guid Department)> SeedItemAsync()
    {
        var departmentId = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
        {
            ["name"] = "Desk",
            ["quantity"] = "3",
            ["price"] = "12.50",
            ["department"] = departmentId.ToString("D"),
        });
        return (id, departmentId);
    }

    private static List<(string Name, string Value)> Values(JsonElement record) =>
        [.. record.GetProperty("values").EnumerateObject().Select(property => (property.Name, property.Value.GetRawText()))];

    /// <summary>The version and the field names in the details of the newest history item.</summary>
    private static (long Version, string Fields) Details(JsonDocument history)
    {
        var details = history.RootElement.GetProperty("items")[0].GetProperty("details");
        return (details.GetProperty("version").GetInt64(), details.GetProperty("fields").GetRawText());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? body)
    {
        using var request = Request(new HttpMethod(method), path, HostA, body, "application/json");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ParseAsync(response);
        }
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
