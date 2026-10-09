using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The computed fields of the RecordsApp fixture: an item's <c>total</c> is
/// <c>quantity * price</c>, and a part's <c>code</c> is <c>upper(name)</c>.
/// </summary>
public sealed class RecordComputedFieldEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_stores_the_computed_value_and_an_update_of_an_input_recomputes_it()
    {
        await fixture.ResetAsync();

        using var created = await PostAsync("""{ "name": "Desk", "quantity": 2, "price": 1.50, "parts": [{ "name": "Leg" }] }""", HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("3.00", Total(created));
        Assert.Equal("""[{"name":"Leg","code":"LEG"}]""", created.RootElement.GetProperty("values").GetProperty("parts").GetRawText());

        using var updated = await PatchAsync(id, """{ "version": 1, "values": { "quantity": 3 } }""", HttpStatusCode.OK);
        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("4.50", Total(updated));

        using var read = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal(2, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("4.50", Total(read));

        // A null input makes the product null.
        using var cleared = await PatchAsync(id, """{ "version": 2, "values": { "price": null } }""", HttpStatusCode.OK);
        Assert.Equal("null", Total(cleared));
    }

    [Fact]
    public async Task Body_that_sets_a_computed_field_is_400_at_that_field_and_writes_nothing()
    {
        await fixture.ResetAsync();

        using var owner = await PostAsync("""{ "name": "Desk", "quantity": 2, "price": 1.50, "total": 3.00 }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/total", "Cannot be set.")], Errors(owner));

        using var ownerNull = await PostAsync("""{ "name": "Desk", "total": null }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/total", "Cannot be set.")], Errors(ownerNull));

        using var row = await PostAsync("""{ "name": "Desk", "parts": [{ "name": "Leg", "code": "LEG" }] }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/parts/0/code", "Cannot be set.")], Errors(row));

        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "ItemPart")).RowCount);

        var id = Id(await PostAsync("""{ "name": "Desk", "quantity": 2, "price": 1.50 }""", HttpStatusCode.Created));
        using var update = await PatchAsync(id, """{ "version": 1, "values": { "total": 9 } }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/total", "Cannot be set.")], Errors(update));
        using var read = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("3.00", Total(read));
    }

    [Fact]
    public async Task Row_stored_before_its_computed_field_reads_null_until_its_next_write()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
        {
            ["name"] = "Desk",
            ["quantity"] = "4",
            ["price"] = "2.25",
        });

        using var before = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal("null", Total(before));

        using var updated = await PatchAsync(id.ToString("D"), """{ "version": 1, "values": {} }""", HttpStatusCode.OK);
        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("9.00", Total(updated));
    }

    [Fact]
    public async Task Value_that_cannot_be_computed_or_does_not_fit_is_400_at_the_computed_field_and_writes_nothing()
    {
        await fixture.ResetAsync();

        // The product overflows what the interpreter holds.
        using var overflow = await PostAsync(
            """{ "name": "Desk", "quantity": 9223372036854775807, "price": 999999999999999999.99 }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/total", "Could not be computed.")], Errors(overflow));

        // The product has more integer digits than the column's precision 20 and scale 2 allow.
        using var tooLarge = await PostAsync(
            """{ "name": "Desk", "quantity": 1000000000000000, "price": 1000.00 }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/total", "Must have at most 18 integer digits.")], Errors(tooLarge));

        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    private static string Total(JsonDocument record) =>
        record.RootElement.GetProperty("values").GetProperty("total").GetRawText();

    private static string Id(JsonDocument record)
    {
        using (record)
        {
            return record.RootElement.GetProperty("id").GetString()!;
        }
    }

    private async Task<JsonDocument> PostAsync(string values, HttpStatusCode expected)
    {
        using var request = Request(HttpMethod.Post, Items, HostA, $$"""{ "values": {{values}} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        if (expected != HttpStatusCode.Created)
        {
            return await ReadProblemAsync(response, expected);
        }

        Assert.Equal(expected, response.StatusCode);
        return await ReadJsonAsync(response);
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
