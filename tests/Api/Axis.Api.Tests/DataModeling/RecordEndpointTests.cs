using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.DataModeling;

[Collection("Api")]
public class RecordEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    private async Task<(HttpClient client, string modelId)> SetupModelAsync(string suffix)
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, suffix);
        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/models",
            new { name = "Contacts", description = (string?)null, icon = (string?)null, color = (string?)null }, Json);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        return (client, body.GetProperty("id").GetString()!);
    }

    // GET /api/models/{id}/records

    [Fact]
    public async Task GetRecords_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync($"/api/models/{Guid.NewGuid()}/records");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRecords_WhenModelHasNoRecords_ReturnsEmptyPage()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec1");

        HttpResponseMessage resp = await client.GetAsync($"/api/models/{modelId}/records");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
        body.GetProperty("records").GetArrayLength().Should().Be(0);
    }

    // POST /api/models/{id}/records

    [Fact]
    public async Task CreateRecord_WhenRequestIsValid_Returns201AndAppearsInList()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec2");

        HttpResponseMessage createResp = await client.PostAsJsonAsync(
            $"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["name"] = "Acme Corp", ["email"] = "acme@example.com" },
            Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        string? id = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();
        id.Should().NotBeNullOrEmpty();

        HttpResponseMessage listResp = await client.GetAsync($"/api/models/{modelId}/records");
        JsonElement page = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        page.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    // GET /api/models/{id}/records/{recordId}

    [Fact]
    public async Task GetRecord_WhenIdIsUnknown_Returns404()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec3");

        HttpResponseMessage resp = await client.GetAsync($"/api/models/{modelId}/records/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRecord_WhenRecordExists_ReturnsRecordData()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec4");

        HttpResponseMessage createResp = await client.PostAsJsonAsync(
            $"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["company"] = "Beta LLC" },
            Json);
        string recordId = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage resp = await client.GetAsync($"/api/models/{modelId}/records/{recordId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement record = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        record.GetProperty("id").GetString().Should().Be(recordId);
        record.TryGetProperty("data", out _).Should().BeTrue();
    }

    // PUT /api/models/{id}/records/{recordId}

    [Fact]
    public async Task UpdateRecord_WhenRequestIsValid_ReturnsNoContent()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec5");

        HttpResponseMessage createResp = await client.PostAsJsonAsync(
            $"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["status"] = "active" },
            Json);
        string recordId = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage updateResp = await client.PutAsJsonAsync(
            $"/api/models/{modelId}/records/{recordId}",
            new Dictionary<string, object?> { ["status"] = "inactive" },
            Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // DELETE /api/models/{id}/records/{recordId}

    [Fact]
    public async Task DeleteRecord_WhenRecordExists_ReturnsNoContentAndRecordDisappears()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec6");

        HttpResponseMessage createResp = await client.PostAsJsonAsync(
            $"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["x"] = 1 },
            Json);
        string recordId = (await createResp.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        HttpResponseMessage deleteResp = await client.DeleteAsync($"/api/models/{modelId}/records/{recordId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage getResp = await client.GetAsync($"/api/models/{modelId}/records/{recordId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Search

    [Fact]
    public async Task GetRecords_WhenSearchTermProvided_FiltersResults()
    {
        (HttpClient client, string modelId) = await SetupModelAsync("rec7");

        await client.PostAsJsonAsync($"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["company"] = "Acme Corp" }, Json);
        await client.PostAsJsonAsync($"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["company"] = "Beta LLC" }, Json);
        await client.PostAsJsonAsync($"/api/models/{modelId}/records",
            new Dictionary<string, object?> { ["company"] = "Acme Subsidiary" }, Json);

        HttpResponseMessage resp = await client.GetAsync($"/api/models/{modelId}/records?search=acme");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement page = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        page.GetProperty("totalCount").GetInt32().Should().Be(2);
    }
}
