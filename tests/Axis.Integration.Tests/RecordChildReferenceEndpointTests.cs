using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A reference field on a child entity of the RecordsApp fixture: an <c>OrderLine</c> row points to
/// a <c>Product</c>. The labels come back with the rows, and the foreign key keeps the product.
/// </summary>
public sealed class RecordChildReferenceEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Orders = "/api/apps/RecordsApp/entities/Order/records";
    private const string Products = "/api/apps/RecordsApp/entities/Product/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_and_read_return_the_label_of_each_row_in_row_order()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");
        var chair = await CreateProductAsync("Chair");

        using var created = await SendAsync(
            HttpMethod.Post,
            Orders,
            $$"""{ "values": { "title": "Office", "lines": [{ "product": "{{chair:D}}", "quantity": 1 }, { "quantity": 5 }, { "product": "{{desk:D}}", "quantity": 2 }] } }""",
            HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString()!;

        Assert.Equal("""[{"product":"Chair"},{},{"product":"Desk"}]""", created.RootElement.GetProperty("labels").GetProperty("lines").GetRawText());
        using var read = await SendAsync(HttpMethod.Get, $"{Orders}/{id}", null, HttpStatusCode.OK);
        Assert.Equal("""[{"product":"Chair"},{},{"product":"Desk"}]""", read.RootElement.GetProperty("labels").GetProperty("lines").GetRawText());
        var lines = read.RootElement.GetProperty("values").GetProperty("lines");
        Assert.Equal(chair, lines[0].GetProperty("product").GetGuid());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("product").ValueKind);

        // An update that replaces the rows gives the labels of the new rows.
        using var updated = await SendAsync(
            HttpMethod.Patch,
            $"{Orders}/{id}",
            $$"""{ "version": 1, "values": { "lines": [{ "product": "{{desk:D}}", "quantity": 1 }] } }""",
            HttpStatusCode.OK);
        Assert.Equal("""[{"product":"Desk"}]""", updated.RootElement.GetProperty("labels").GetProperty("lines").GetRawText());
    }

    [Fact]
    public async Task The_child_table_has_a_foreign_key_to_the_product_table()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("OrderLine", out var line));

        var name = EntityNaming.ForeignKey(EntityNaming.Table(line.Id), EntityNaming.Column("product"));

        var count = await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM pg_constraint WHERE conname = '{name}' AND contype = 'f'");
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task A_product_a_row_points_to_cannot_be_deleted_until_the_order_is()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");
        using var order = await SendAsync(
            HttpMethod.Post,
            Orders,
            $$"""{ "values": { "title": "Office", "lines": [{ "product": "{{desk:D}}", "quantity": 1 }] } }""",
            HttpStatusCode.Created);
        var orderId = order.RootElement.GetProperty("id").GetString()!;

        using var refused = await DeleteAsync($"{Products}/{desk:D}");
        using var problem = await ReadProblemAsync(refused, HttpStatusCode.Conflict);
        Assert.Equal("Another record references this record.", problem.RootElement.GetProperty("title").GetString());

        using var orderDeleted = await DeleteAsync($"{Orders}/{orderId}");
        Assert.Equal(HttpStatusCode.NoContent, orderDeleted.StatusCode);
        using var productDeleted = await DeleteAsync($"{Products}/{desk:D}");
        Assert.Equal(HttpStatusCode.NoContent, productDeleted.StatusCode);
    }

    [Fact]
    public async Task A_row_that_names_no_product_is_a_400_at_its_cell()
    {
        await fixture.ResetAsync();
        var desk = await CreateProductAsync("Desk");

        using var response = await PostRawAsync(
            $$"""{ "values": { "title": "Office", "lines": [{ "product": "{{desk:D}}" }, { "product": "{{Guid.NewGuid():D}}" }] } }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        var error = Assert.Single(errors.EnumerateObject());
        Assert.Equal("/values/lines/1/product", error.Name);
        Assert.Equal("No record has this id.", error.Value[0].GetString());
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "OrderLine")).RowCount);
    }

    private async Task<Guid> CreateProductAsync(string name)
    {
        using var created = await SendAsync(HttpMethod.Post, Products, $$"""{ "values": { "name": "{{name}}" } }""", HttpStatusCode.Created);
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? body, HttpStatusCode expected)
    {
        using var request = body is null ? Request(method, path, HostA) : Request(method, path, HostA, body);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<HttpResponseMessage> PostRawAsync(string body)
    {
        using var request = Request(HttpMethod.Post, Orders, HostA, body);
        return await fixture.Client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> DeleteAsync(string path)
    {
        using var request = Request(HttpMethod.Delete, path, HostA);
        return await fixture.Client.SendAsync(request, CancellationToken);
    }
}
