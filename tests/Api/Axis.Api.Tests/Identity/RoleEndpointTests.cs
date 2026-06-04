using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class RoleEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // GET /api/roles

    [Fact]
    public async Task GetRoles_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRoles_WhenAuthenticated_ReturnsFourSeededSystemRoles()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "role1");

        HttpResponseMessage resp = await client.GetAsync("/api/roles");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> items = body.GetProperty("items").EnumerateArray().ToList();

        // Admin, Editor, Viewer, End User seeded at registration
        items.Should().HaveCount(4);
        items.Select(r => r.GetProperty("name").GetString()!).Should()
            .Contain(["Admin", "Editor", "Viewer", "End User"]);
    }

    [Fact]
    public async Task GetRoles_WhenAuthenticated_SystemRolesAreFlagged()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "role2");

        HttpResponseMessage resp = await client.GetAsync("/api/roles");
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> roles = body.GetProperty("items").EnumerateArray().ToList();

        roles.All(r => r.GetProperty("isSystem").GetBoolean()).Should().BeTrue();
    }

    // POST /api/roles

    [Fact]
    public async Task CreateRole_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync("/api/roles",
            new { name = "Analyst", permissions = new[] { "data_modeling:model:read" } }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRole_WhenRequestIsValid_ReturnsIdAndAppearsInList()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "role3");

        // Create
        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/roles", new
        {
            name = "Analyst",
            description = "Read-only analyst",
            permissions = new[] { "data_modeling:model:read", "data_modeling:record:read" },
        }, Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string? newId = createBody.GetProperty("id").GetString();
        newId.Should().NotBeNullOrEmpty();

        // Verify appears in list
        HttpResponseMessage listResp = await client.GetAsync("/api/roles");
        JsonElement listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        List<JsonElement> roles = listBody.GetProperty("items").EnumerateArray().ToList();

        roles.Should().HaveCount(5); // 4 system + 1 custom
        roles.Select(r => r.GetProperty("name").GetString()).Should().Contain("Analyst");
    }

    // PUT /api/roles/{roleId}

    [Fact]
    public async Task UpdateRole_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PutAsJsonAsync($"/api/roles/{Guid.NewGuid()}",
            new { name = "X", permissions = Array.Empty<string>() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateRole_WhenRoleIsCustom_ReturnsNoContent()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "role4");

        // Create a custom role first
        HttpResponseMessage createResp = await client.PostAsJsonAsync("/api/roles", new
        {
            name = "Temp",
            permissions = new[] { "data_modeling:model:read" },
        }, Json);
        JsonElement createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string roleId = createBody.GetProperty("id").GetString()!;

        // Update it
        HttpResponseMessage updateResp = await client.PutAsJsonAsync($"/api/roles/{roleId}", new
        {
            name = "Updated",
            description = "Updated description",
            permissions = new[] { "data_modeling:model:read", "data_modeling:record:read" },
        }, Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
