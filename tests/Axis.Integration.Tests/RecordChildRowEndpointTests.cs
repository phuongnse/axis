using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordChildRowEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string ItemParts = "/api/apps/RecordsApp/entities/ItemPart/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_with_rows_returns_them_in_order_on_create_and_on_read()
    {
        await fixture.ResetAsync();

        using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Table", "parts": [{ "name": "Leg" }, { "name": "Top" }] } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await ReadJsonAsync(response);
        using var read = await GetJsonAsync(response.Headers.Location!.OriginalString);
        foreach (var record in new[] { created, read })
        {
            Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
            var values = record.RootElement.GetProperty("values");
            Assert.Equal(
                ["name", "quantity", "price", "active", "neededBy", "orderedAt", "status", "parts", "department"],
                values.EnumerateObject().Select(property => property.Name));
            Assert.Equal("""[{"name":"Leg"},{"name":"Top"}]""", values.GetProperty("parts").GetRawText());
        }

        Assert.Equal(2, (await fixture.TableShapeAsync(TenantA, "ItemPart")).RowCount);

        // A list leaves the collections out.
        using var list = await GetJsonAsync(Items);
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("values").TryGetProperty("parts", out _));
    }

    [Fact]
    public async Task Invalid_row_is_a_400_problem_at_its_path_and_writes_neither_the_owner_nor_any_row()
    {
        await fixture.ResetAsync();

        using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Table", "parts": [{ "name": "Leg" }, { "name": 5 }] } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["/values/parts/1/name"], ErrorKeys(problem));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "ItemPart")).RowCount);
    }

    [Fact]
    public async Task Update_with_rows_replaces_them_and_bumps_the_version_and_a_stale_version_changes_nothing()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync("""{ "name": "Table", "parts": [{ "name": "Leg" }] }""");

        using var updated = await PatchAsync(id, """{ "version": 1, "values": { "parts": [{ "name": "A" }, { "name": "B" }] } }""", HttpStatusCode.OK);

        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("""[{"name":"A"},{"name":"B"}]""", updated.RootElement.GetProperty("values").GetProperty("parts").GetRawText());

        using var stale = await PatchAsync(id, """{ "version": 1, "values": { "parts": [{ "name": "C" }] } }""", HttpStatusCode.Conflict);

        Assert.Equal("The record has changed since this version was read.", stale.RootElement.GetProperty("title").GetString());
        using var afterStale = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal(2, afterStale.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("""[{"name":"A"},{"name":"B"}]""", afterStale.RootElement.GetProperty("values").GetProperty("parts").GetRawText());

        // A body that leaves the collection out keeps its rows.
        using var kept = await PatchAsync(id, """{ "version": 2, "values": { "quantity": 4 } }""", HttpStatusCode.OK);

        Assert.Equal(3, kept.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("4", kept.RootElement.GetProperty("values").GetProperty("quantity").GetRawText());
        Assert.Equal("""[{"name":"A"},{"name":"B"}]""", kept.RootElement.GetProperty("values").GetProperty("parts").GetRawText());

        using var emptied = await PatchAsync(id, """{ "version": 3, "values": { "parts": [] } }""", HttpStatusCode.OK);

        Assert.Equal(4, emptied.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("[]", emptied.RootElement.GetProperty("values").GetProperty("parts").GetRawText());
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "ItemPart")).RowCount);
    }

    [Fact]
    public async Task Row_refused_by_storage_rolls_back_the_owner_and_every_row()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync("""{ "name": "Table", "parts": [{ "name": "Leg" }] }""");
        Assert.True(fixture.Model.TryGetEntity("ItemPart", out var part));
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(part.Id));

        // The fixture is shared by the class, so the column is dropped whatever the outcome.
        await fixture.ExecuteAsync(TenantA, $"""ALTER TABLE {table} ADD COLUMN "f_extra" text NOT NULL DEFAULT 'x'""");
        await fixture.ExecuteAsync(TenantA, $"""ALTER TABLE {table} ALTER COLUMN "f_extra" DROP DEFAULT""");
        try
        {
            using var create = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Chair", "parts": [{ "name": "Seat" }] } }""");
            using var createResponse = await fixture.Client.SendAsync(create, CancellationToken);
            using var update = Request(HttpMethod.Patch, $"{Items}/{id}", HostA, """{ "version": 1, "values": { "name": "Desk", "parts": [{ "name": "Top" }] } }""");
            using var updateResponse = await fixture.Client.SendAsync(update, CancellationToken);

            foreach (var response in new[] { createResponse, updateResponse })
            {
                using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
                Assert.Equal("The stored schema does not match the active model.", problem.RootElement.GetProperty("title").GetString());
            }
        }
        finally
        {
            await fixture.ExecuteAsync(TenantA, $"""ALTER TABLE {table} DROP COLUMN "f_extra" """);
        }

        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        using var record = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("\"Table\"", record.RootElement.GetProperty("values").GetProperty("name").GetRawText());
        Assert.Equal("""[{"name":"Leg"}]""", record.RootElement.GetProperty("values").GetProperty("parts").GetRawText());
    }

    [Fact]
    public async Task Delete_of_the_owner_deletes_its_rows()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync("""{ "name": "Table", "parts": [{ "name": "Leg" }, { "name": "Top" }] }""");

        using var request = Request(HttpMethod.Delete, $"{Items}/{id}", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "ItemPart")).RowCount);
    }

    public static TheoryData<string, bool, bool> ChildEntityRoutes => new()
    {
        { "GET", false, false },
        { "GET", true, false },
        { "POST", false, true },
        { "PATCH", true, true },
        { "DELETE", true, false },
    };

    [Theory]
    [MemberData(nameof(ChildEntityRoutes))]
    public async Task Record_routes_of_a_child_entity_are_404_problems(string method, bool byId, bool withBody)
    {
        await fixture.ResetAsync();
        var path = byId ? $"{ItemParts}/{Guid.NewGuid():D}" : ItemParts;
        var body = withBody ? """{ "version": 1, "values": { "name": "Leg" } }""" : null;

        using var request = Request(new HttpMethod(method), path, HostA, body);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal("The application has no entity with this name.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "ItemPart")).RowCount);
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

    private static List<string> ErrorKeys(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
}
