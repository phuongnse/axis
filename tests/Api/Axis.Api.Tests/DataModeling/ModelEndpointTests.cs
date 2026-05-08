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
    public async Task GetModels_without_token_returns_401()
    {
        var resp = await fixture.Client.GetAsync("/api/models");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_returns_empty_list_for_new_org()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl1");

        var resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement[]>(Json);
        body.Should().NotBeNull().And.BeEmpty();
    }

    // POST /api/models

    [Fact]
    public async Task CreateModel_without_token_returns_401()
    {
        var resp = await fixture.Client.PostAsJsonAsync("/api/models",
            new { name = "Contacts" }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateModel_returns_201_and_appears_in_list()
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
        var models = (await listResp.Content.ReadFromJsonAsync<JsonElement[]>(Json))!;
        models.Should().HaveCount(1);
        models[0].GetProperty("name").GetString().Should().Be("Contacts");
    }

    // GET /api/models/{id}

    [Fact]
    public async Task GetModel_returns_404_for_unknown_id()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "mdl3");

        var resp = await client.GetAsync($"/api/models/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetModel_returns_model_with_system_fields()
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
    public async Task UpdateModel_returns_no_content()
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
    public async Task DeleteModel_returns_no_content_and_model_disappears()
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
    public async Task AddField_returns_201_and_field_appears_in_model()
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
    public async Task ReorderFields_returns_no_content()
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
