using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The aggregates of the RecordsApp fixture: an order's <c>total</c> is
/// <c>sum(lines, amount)</c> over each line's computed <c>quantity * unitPrice</c>, its
/// <c>lineCount</c> is <c>count(lines)</c>, and its validation <c>count(lines) &gt;= 1</c> is
/// reported at <c>lines</c>.
/// </summary>
public sealed class RecordAggregateEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Orders = "/api/apps/RecordsApp/entities/Order/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_write_through_the_owner_stores_the_aggregates_of_the_rows_it_stores()
    {
        await fixture.ResetAsync();

        using var created = await PostAsync(
            """{ "title": "Desk", "lines": [{ "quantity": 1, "unitPrice": 1.50 }, { "quantity": 2, "unitPrice": 1.00 }] }""",
            HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString()!;
        Assert.Equal(("3.50", 2), Totals(created));
        using var createdRead = await GetJsonAsync($"{Orders}/{id}");
        Assert.Equal(("3.50", 2), Totals(createdRead));

        // Adding a row.
        using var added = await PatchAsync(
            id,
            """{ "version": 1, "values": { "lines": [{ "quantity": 1, "unitPrice": 1.50 }, { "quantity": 2, "unitPrice": 1.00 }, { "quantity": 3, "unitPrice": 0.25 }] } }""",
            HttpStatusCode.OK);
        Assert.Equal(("4.25", 3), Totals(added));
        Assert.Equal(3, (await fixture.TableShapeAsync(TenantA, "OrderLine")).RowCount);

        // Changing a row.
        using var changed = await PatchAsync(
            id,
            """{ "version": 2, "values": { "lines": [{ "quantity": 1, "unitPrice": 1.50 }, { "quantity": 4, "unitPrice": 1.00 }, { "quantity": 3, "unitPrice": 0.25 }] } }""",
            HttpStatusCode.OK);
        Assert.Equal(("6.25", 3), Totals(changed));
        using var changedRead = await GetJsonAsync($"{Orders}/{id}");
        Assert.Equal(("6.25", 3), Totals(changedRead));

        // Removing rows down to one, which the order's validation still allows.
        using var removed = await PatchAsync(id, """{ "version": 3, "values": { "lines": [{ "quantity": 2, "unitPrice": 0.75 }] } }""", HttpStatusCode.OK);
        Assert.Equal(("1.50", 1), Totals(removed));
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "OrderLine")).RowCount);

        // An update that leaves the rows out recomputes the totals from the stored rows.
        using var renamed = await PatchAsync(id, """{ "version": 4, "values": { "title": "Renamed" } }""", HttpStatusCode.OK);
        Assert.Equal(5, renamed.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(("1.50", 1), Totals(renamed));
        using var read = await GetJsonAsync($"{Orders}/{id}");
        Assert.Equal("Renamed", read.RootElement.GetProperty("values").GetProperty("title").GetString());
        Assert.Equal(("1.50", 1), Totals(read));
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "OrderLine")).RowCount);
    }

    [Fact]
    public async Task Validation_over_the_rows_refuses_a_write_that_leaves_no_row_at_the_collection()
    {
        await fixture.ResetAsync();

        using var withoutLines = await PostAsync("""{ "title": "Desk" }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/lines", "order.needsLine")], Errors(withoutLines));
        using var emptyLines = await PostAsync("""{ "title": "Desk", "lines": [] }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/lines", "order.needsLine")], Errors(emptyLines));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Order")).RowCount);

        using var created = await PostAsync("""{ "title": "Desk", "lines": [{ "quantity": 1, "unitPrice": 2.00 }] }""", HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString()!;

        using var emptied = await PatchAsync(id, """{ "version": 1, "values": { "lines": [] } }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/lines", "order.needsLine")], Errors(emptied));
        using var read = await GetJsonAsync($"{Orders}/{id}");
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(1, read.RootElement.GetProperty("values").GetProperty("lines").GetArrayLength());
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "OrderLine")).RowCount);

        // The stored row satisfies the validation when the update leaves the rows out.
        using var renamed = await PatchAsync(id, """{ "version": 1, "values": { "title": "Renamed" } }""", HttpStatusCode.OK);
        Assert.Equal(2, renamed.RootElement.GetProperty("version").GetInt64());
    }

    private static (string Total, int LineCount) Totals(JsonDocument record)
    {
        var values = record.RootElement.GetProperty("values");
        return (values.GetProperty("total").GetRawText(), values.GetProperty("lineCount").GetInt32());
    }

    private async Task<JsonDocument> PostAsync(string values, HttpStatusCode expected)
    {
        using var request = Request(HttpMethod.Post, Orders, HostA, $$"""{ "values": {{values}} }""");
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
        using var request = Request(HttpMethod.Patch, $"{Orders}/{id}", HostA, body);
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
