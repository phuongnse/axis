using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.DataModeling;

[Collection("Api")]
public class ModelEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // GET /api/models

    [Fact]
    public async Task GetModels_WhenNoToken_Returns401()
    {
        var resp = await fixture.Client.GetAsync("/api/models");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_WhenOrgHasNoModels_ReturnsEmptyList()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl1");

        var resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().BeEmpty();
    }

    // POST /api/models

    [Fact]
    public async Task CreateModel_WhenNoToken_Returns401()
    {
        var resp = await fixture.Client.PostAsJsonAsync("/api/models",
            new { name = "Contacts" }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateModel_WhenRequestIsValid_Returns201AndAppearsInList()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl2");

        var createResp = await client.PostAsJsonAsync("/api/models", new
        {
            name = "Contacts",
            description = "Contact records",
            icon = (string?)null,
            color = (string?)null,
        }, Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = body.GetProperty("id").GetString();
        id.Should().NotBeNullOrEmpty();

        var listResp = await client.GetAsync("/api/models");
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var models = listBody.GetProperty("items").EnumerateArray().ToList();
        models.Should().HaveCount(1);
        models[0].GetProperty("name").GetString().Should().Be("Contacts");
    }

    // GET /api/models/{id}

    [Fact]
    public async Task GetModel_WhenIdIsUnknown_Returns404()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl3");

        var resp = await client.GetAsync($"/api/models/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetModel_WhenModelExists_ReturnsModelWithSystemFields()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl4");

        var createResp = await client.PostAsJsonAsync("/api/models",
            new { name = "Orders", description = (string?)null, icon = (string?)null, color = (string?)null }, Json);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = createBody.GetProperty("id").GetString()!;

        var resp = await client.GetAsync($"/api/models/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var model = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        model.GetProperty("name").GetString().Should().Be("Orders");

        // System fields (id, created_at, updated_at) should be present
        var fields = model.GetProperty("fields").EnumerateArray().ToList();
        fields.Should().NotBeEmpty();
        fields.Should().Contain(f => f.GetProperty("is_system").GetBoolean());
    }

    // PUT /api/models/{id}

    [Fact]
    public async Task UpdateModel_WhenRequestIsValid_ReturnsNoContent()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl5");

        var createResp = await client.PostAsJsonAsync("/api/models",
            new { name = "Items", description = (string?)null, icon = (string?)null, color = (string?)null }, Json);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var updateResp = await client.PutAsJsonAsync($"/api/models/{id}", new
        {
            name = "Products",
            description = "Product catalog",
            icon = "📦",
            color = "#FF5733",
        }, Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await (await client.GetAsync($"/api/models/{id}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("name").GetString().Should().Be("Products");
    }

    // DELETE /api/models/{id}

    [Fact]
    public async Task DeleteModel_WhenModelExists_ReturnsNoContentAndModelDisappears()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl6");

        var createResp = await client.PostAsJsonAsync("/api/models",
            new { name = "Temp", description = (string?)null, icon = (string?)null, color = (string?)null }, Json);
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var deleteResp = await client.DeleteAsync($"/api/models/{id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await client.GetAsync($"/api/models/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // POST /api/models/{id}/fields

    [Fact]
    public async Task AddField_WhenRequestIsValid_Returns201AndFieldAppearsInModel()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl7");

        var createResp = await client.PostAsJsonAsync("/api/models",
            new { name = "Invoices", description = (string?)null, icon = (string?)null, color = (string?)null }, Json);
        var modelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var addFieldResp = await client.PostAsJsonAsync($"/api/models/{modelId}/fields", new
        {
            name = "amount",
            label = "Amount",
            type = "Number",
            is_required = true,
            config = new { min = 0, decimal_places = 2 },
        }, Json);

        addFieldResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var fieldBody = await addFieldResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        fieldBody.GetProperty("id").GetString().Should().NotBeNullOrEmpty();

        var model = await (await client.GetAsync($"/api/models/{modelId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
        var fields = model.GetProperty("fields").EnumerateArray().ToList();
        fields.Should().Contain(f => f.GetProperty("name").GetString() == "amount");
    }

    // PUT /api/models/{id}/fields/order

    [Fact]
    public async Task ReorderFields_WhenRequestIsValid_ReturnsNoContent()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl8");

        var createResp = await client.PostAsJsonAsync("/api/models",
            new { name = "Tasks", description = (string?)null, icon = (string?)null, color = (string?)null }, Json);
        var modelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var f1Resp = await client.PostAsJsonAsync($"/api/models/{modelId}/fields",
            new { name = "title", label = "Title", type = "Text", is_required = true, config = new { } }, Json);
        var f1Id = (await f1Resp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var f2Resp = await client.PostAsJsonAsync($"/api/models/{modelId}/fields",
            new { name = "due_date", label = "Due Date", type = "Date", is_required = false, config = new { } }, Json);
        var f2Id = (await f2Resp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        var reorderResp = await client.PutAsJsonAsync($"/api/models/{modelId}/fields/order",
            new { field_ids = new[] { f2Id, f1Id } }, Json);

        reorderResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
