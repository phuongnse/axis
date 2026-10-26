using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A <c>references</c> field of the RecordsApp fixture: a <c>Bundle</c> holds a set of
/// <c>Product</c> ids. A write replaces the set, a single read returns the ids and their labels in
/// written order, and a list leaves the field out.
/// </summary>
public sealed class RecordReferencesEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Bundles = "/api/apps/RecordsApp/entities/Bundle/records";
    private const string Products = "/api/apps/RecordsApp/entities/Product/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_replaces_the_set_and_a_read_returns_ids_and_labels_in_written_order()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");
        var chair = await CreateProductAsync("Chair");
        var lamp = await CreateProductAsync("Lamp");

        using var created = await SendAsync(
            HttpMethod.Post,
            Bundles,
            $$"""{ "values": { "title": "Office", "products": ["{{lamp:D}}", "{{desk:D}}", "{{chair:D}}"] } }""",
            HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString()!;
        AssertSet(created, [lamp, desk, chair], ["Lamp", "Desk", "Chair"]);

        using var read = await SendAsync(HttpMethod.Get, $"{Bundles}/{id}", null, HttpStatusCode.OK);
        AssertSet(read, [lamp, desk, chair], ["Lamp", "Desk", "Chair"]);

        using var updated = await SendAsync(
            HttpMethod.Patch,
            $"{Bundles}/{id}",
            $$"""{ "version": 1, "values": { "products": ["{{chair:D}}", "{{lamp:D}}"] } }""",
            HttpStatusCode.OK);
        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
        AssertSet(updated, [chair, lamp], ["Chair", "Lamp"]);

        using var reread = await SendAsync(HttpMethod.Get, $"{Bundles}/{id}", null, HttpStatusCode.OK);
        AssertSet(reread, [chair, lamp], ["Chair", "Lamp"]);

        // An empty array clears the set, and a stale version leaves it alone.
        using var stale = await SendAsync(
            HttpMethod.Patch, $"{Bundles}/{id}", """{ "version": 1, "values": { "products": [] } }""", HttpStatusCode.Conflict);
        using var cleared = await SendAsync(
            HttpMethod.Patch, $"{Bundles}/{id}", """{ "version": 2, "values": { "products": [] } }""", HttpStatusCode.OK);
        AssertSet(cleared, [], []);
    }

    [Fact]
    public async Task A_duplicate_an_unknown_id_and_too_many_ids_are_a_400_and_a_list_leaves_the_field_out()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");
        var chair = await CreateProductAsync("Chair");

        await AssertRefusedAsync($$"""["{{desk:D}}", "{{desk:D}}"]""", "/values/products/1", "Duplicate id.");
        await AssertRefusedAsync($$"""["{{desk:D}}", "{{Guid.NewGuid():D}}"]""", "/values/products/1", "No record has this id.");
        await AssertRefusedAsync(
            $"[{string.Join(", ", Enumerable.Range(0, 1001).Select(_ => $"\"{Guid.NewGuid():D}\""))}]",
            "/values/products",
            "Must hold at most 1000 ids.");
        await AssertRefusedAsync("null", "/values/products", "Must be an array of record ids.");
        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {BundleTable}"));

        using var created = await SendAsync(
            HttpMethod.Post,
            Bundles,
            $$"""{ "values": { "title": "Office", "products": ["{{desk:D}}", "{{chair:D}}"] } }""",
            HttpStatusCode.Created);
        Assert.True(created.RootElement.GetProperty("values").TryGetProperty("products", out _));

        using var list = await SendAsync(HttpMethod.Get, Bundles, null, HttpStatusCode.OK);
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("values").TryGetProperty("products", out _));
        Assert.False(item.TryGetProperty("labels", out var labels) && labels.TryGetProperty("products", out _));
    }

    [Fact]
    public async Task A_linked_product_cannot_be_deleted_and_deleting_the_bundle_removes_its_links()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");
        using var bundle = await SendAsync(
            HttpMethod.Post,
            Bundles,
            $$"""{ "values": { "title": "Office", "products": ["{{desk:D}}"] } }""",
            HttpStatusCode.Created);
        var bundleId = bundle.RootElement.GetProperty("id").GetString()!;

        using var refused = await DeleteAsync($"{Products}/{desk:D}");
        using var problem = await ReadProblemAsync(refused, HttpStatusCode.Conflict);
        Assert.Equal("Another record references this record.", problem.RootElement.GetProperty("title").GetString());

        Assert.True(fixture.Model.TryGetEntity("Bundle", out var entity));
        var junction = EntityNaming.QualifiedTable(EntityNaming.Junction(entity.Id, "products"));
        Assert.Equal(1L, await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {junction}"));

        using var deleted = await DeleteAsync($"{Bundles}/{bundleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {junction}"));
        using var productDeleted = await DeleteAsync($"{Products}/{desk:D}");
        Assert.Equal(HttpStatusCode.NoContent, productDeleted.StatusCode);
    }

    [Fact]
    public async Task A_read_grant_hides_the_set_and_its_labels_and_a_write_grant_guards_it()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");
        using var bundle = await SendAsync(
            HttpMethod.Post,
            Bundles,
            $$"""{ "values": { "title": "Office", "products": ["{{desk:D}}"] } }""",
            HttpStatusCode.Created);
        var id = bundle.RootElement.GetProperty("id").GetString()!;

        // fia reads and writes the title only, so the set and its labels are left out.
        using var fia = fixture.CreateClient().SignedInAs("fia");
        using (var read = await SendAsAsync(fia, HttpMethod.Get, $"{Bundles}/{id}", null, HttpStatusCode.OK))
        {
            Assert.False(read.RootElement.GetProperty("values").TryGetProperty("products", out _));
            Assert.False(read.RootElement.GetProperty("labels").TryGetProperty("products", out _));
        }

        using (var refused = await SendRawAsync(
            fia, HttpMethod.Patch, $"{Bundles}/{id}", $$"""{ "version": 1, "values": { "products": ["{{desk:D}}"] } }"""))
        {
            using var problem = await ReadProblemAsync(refused, HttpStatusCode.Forbidden);
            Assert.Equal(["/values/products"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        // rita reads the set, but holds no grant on products, so the labels are left out.
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var ids = await SendAsAsync(rita, HttpMethod.Get, $"{Bundles}/{id}", null, HttpStatusCode.OK);
        Assert.Equal([desk], ids.RootElement.GetProperty("values").GetProperty("products").EnumerateArray().Select(value => value.GetGuid()));
        Assert.False(ids.RootElement.GetProperty("labels").TryGetProperty("products", out _));
    }

    private string BundleTable
    {
        get
        {
            Assert.True(fixture.Model.TryGetEntity("Bundle", out var entity));
            return EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));
        }
    }

    private static void AssertSet(JsonDocument record, Guid[] ids, string[] labels)
    {
        Assert.Equal(ids, record.RootElement.GetProperty("values").GetProperty("products").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal(labels, record.RootElement.GetProperty("labels").GetProperty("products").EnumerateArray().Select(label => label.GetString()));
    }

    private async Task AssertRefusedAsync(string products, string pointer, string message)
    {
        using var request = Request(HttpMethod.Post, Bundles, HostA, $$"""{ "values": { "title": "Office", "products": {{products}} } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(pointer, error.Name);
        Assert.Equal(message, error.Value[0].GetString());
    }

    private async Task<Guid> CreateProductAsync(string name)
    {
        using var created = await SendAsync(HttpMethod.Post, Products, $$"""{ "values": { "name": "{{name}}" } }""", HttpStatusCode.Created);
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private Task<JsonDocument> SendAsync(HttpMethod method, string path, string? body, HttpStatusCode expected) =>
        SendAsAsync(fixture.Client, method, path, body, expected);

    private static async Task<JsonDocument> SendAsAsync(HttpClient client, HttpMethod method, string path, string? body, HttpStatusCode expected)
    {
        using var response = await SendRawAsync(client, method, path, body);
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task<HttpResponseMessage> SendRawAsync(HttpClient client, HttpMethod method, string path, string? body)
    {
        using var request = body is null ? Request(method, path, HostA) : Request(method, path, HostA, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> DeleteAsync(string path)
    {
        using var request = Request(HttpMethod.Delete, path, HostA);
        return await fixture.Client.SendAsync(request, CancellationToken);
    }
}
