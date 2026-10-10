using System.Globalization;
using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The validations of the RecordsApp fixture: an item's quantity is positive, its price is not
/// negative, a closed item is not active, and a part name does not start with a space. A
/// ticket's due time is in the future.
/// </summary>
public sealed class RecordValidationEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Tickets = "/api/apps/RecordsApp/entities/Ticket/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_that_breaks_rules_is_400_with_every_failing_rule_and_writes_nothing()
    {
        await fixture.ResetAsync();

        using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk", "quantity": 0, "price": -1 } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(
            [("/values/price", "item.priceNotNegative"), ("/values/quantity", "item.quantityPositive")],
            Errors(problem));
        using var list = await GetJsonAsync(Items);
        Assert.Equal(0, list.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Create_and_update_that_pass_every_rule_are_stored()
    {
        await fixture.ResetAsync();

        var id = await CreateItemAsync("""{ "name": "Desk", "quantity": 2, "price": 0, "status": "closed", "parts": [{ "name": "Leg" }] }""");
        using var updated = await PatchAsync(id, """{ "version": 1, "values": { "quantity": 5, "active": false } }""", HttpStatusCode.OK);

        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("5", updated.RootElement.GetProperty("values").GetProperty("quantity").GetRawText());
    }

    [Fact]
    public async Task Update_that_breaks_a_rule_is_400_and_leaves_the_record_unchanged()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync("""{ "name": "Desk", "quantity": 2 }""");

        using var problem = await PatchAsync(id, """{ "version": 1, "values": { "quantity": 0 } }""", HttpStatusCode.BadRequest);

        Assert.Equal([("/values/quantity", "item.quantityPositive")], Errors(problem));
        using var read = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("2", read.RootElement.GetProperty("values").GetProperty("quantity").GetRawText());
    }

    [Fact]
    public async Task Update_of_an_unrelated_field_is_checked_against_the_merged_record()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync("""{ "name": "Lamp", "status": "closed", "active": false }""");

        // The body changes only active, but the stored status makes the rule on status fail.
        using var problem = await PatchAsync(id, """{ "version": 1, "values": { "active": true } }""", HttpStatusCode.BadRequest);

        Assert.Equal([("/values/status", "item.closedInactive")], Errors(problem));
    }

    [Fact]
    public async Task Child_row_that_breaks_a_rule_is_400_at_its_row_path()
    {
        await fixture.ResetAsync();

        using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk", "parts": [{ "name": "Leg" }, { "name": " Top" }] } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/parts/1/name", "itemPart.noLeadingSpace")], Errors(problem));

        var id = await CreateItemAsync("""{ "name": "Desk", "parts": [{ "name": "Leg" }] }""");
        using var update = await PatchAsync(id, """{ "version": 1, "values": { "parts": [{ "name": " Top" }] } }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/parts/0/name", "itemPart.noLeadingSpace")], Errors(update));
    }

    [Fact]
    public async Task Update_of_an_unknown_record_is_404_before_any_validation()
    {
        await fixture.ResetAsync();

        using var problem = await PatchAsync(
            "6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7", """{ "version": 1, "values": { "quantity": 0 } }""", HttpStatusCode.NotFound);

        Assert.Equal("No record has this id.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Update_with_a_stale_version_that_passes_every_rule_is_still_409()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync("""{ "name": "Desk", "quantity": 2 }""");
        using var first = await PatchAsync(id, """{ "version": 1, "values": { "quantity": 3 } }""", HttpStatusCode.OK);

        using var problem = await PatchAsync(id, """{ "version": 1, "values": { "quantity": 4 } }""", HttpStatusCode.Conflict);

        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Validation_that_compares_with_now_refuses_a_past_time_and_accepts_a_future_one()
    {
        await fixture.ResetAsync();
        // A day either side, so clock skew between the test and the database cannot flip the result.
        var past = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var future = DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        using var refused = Request(HttpMethod.Post, Tickets, HostA, $$"""{ "values": { "title": "Late", "dueAt": "{{past}}" } }""");
        using var refusedResponse = await fixture.Client.SendAsync(refused, CancellationToken);
        using var problem = await ReadProblemAsync(refusedResponse, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/dueAt", "ticket.dueInPast")], Errors(problem));

        using var accepted = Request(HttpMethod.Post, Tickets, HostA, $$"""{ "values": { "title": "On time", "dueAt": "{{future}}" } }""");
        using var acceptedResponse = await fixture.Client.SendAsync(accepted, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, acceptedResponse.StatusCode);
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Ticket")).RowCount);
    }

    private async Task<string> CreateItemAsync(string values)
    {
        using var request = Request(HttpMethod.Post, Items, HostA, $$"""{ "values": {{values}} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var record = await ReadJsonAsync(response);
        return record.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonDocument> PatchAsync(string id, string body, HttpStatusCode expected)
    {
        using var request = Request(HttpMethod.Patch, $"{Items}/{id}", HostA, body);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        if (expected != HttpStatusCode.OK)
        {
            return await ReadProblemAsync(response, expected);
        }

        Assert.Equal(expected, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
