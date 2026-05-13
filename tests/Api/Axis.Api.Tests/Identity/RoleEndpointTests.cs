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
    public async Task GetRoles_without_token_returns_401()
    {
        var resp = await fixture.Client.GetAsync("/api/roles");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRoles_returns_four_seeded_system_roles()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "role1");

        var resp = await client.GetAsync("/api/roles");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        // Admin, Editor, Viewer, End User seeded at registration
        items.Should().HaveCount(4);
        items.Select(r => r.GetProperty("name").GetString()!).Should()
            .Contain(["Admin", "Editor", "Viewer", "End User"]);
    }

    [Fact]
    public async Task GetRoles_system_roles_are_flagged()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "role2");

        var resp = await client.GetAsync("/api/roles");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var roles = body.GetProperty("items").EnumerateArray().ToList();

        roles.All(r => r.GetProperty("is_system").GetBoolean()).Should().BeTrue();
    }

    // POST /api/roles

    [Fact]
    public async Task CreateRole_without_token_returns_401()
    {
        var resp = await fixture.Client.PostAsJsonAsync("/api/roles",
            new { name = "Analyst", permissions = new[] { "data_modeling:model:read" } }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRole_returns_id_and_appears_in_list()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "role3");

        // Create
        var createResp = await client.PostAsJsonAsync("/api/roles", new
        {
            name = "Analyst",
            description = "Read-only analyst",
            permissions = new[] { "data_modeling:model:read", "data_modeling:record:read" },
        }, Json);

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var newId = createBody.GetProperty("id").GetString();
        newId.Should().NotBeNullOrEmpty();

        // Verify appears in list
        var listResp = await client.GetAsync("/api/roles");
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var roles = listBody.GetProperty("items").EnumerateArray().ToList();

        roles.Should().HaveCount(5); // 4 system + 1 custom
        roles.Select(r => r.GetProperty("name").GetString()).Should().Contain("Analyst");
    }

    // PUT /api/roles/{roleId}

    [Fact]
    public async Task UpdateRole_without_token_returns_401()
    {
        var resp = await fixture.Client.PutAsJsonAsync($"/api/roles/{Guid.NewGuid()}",
            new { name = "X", permissions = Array.Empty<string>() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateRole_custom_role_returns_no_content()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "role4");

        // Create a custom role first
        var createResp = await client.PostAsJsonAsync("/api/roles", new
        {
            name = "Temp",
            permissions = new[] { "data_modeling:model:read" },
        }, Json);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var roleId = createBody.GetProperty("id").GetString()!;

        // Update it
        var updateResp = await client.PutAsJsonAsync($"/api/roles/{roleId}", new
        {
            name = "Updated",
            description = "Updated description",
            permissions = new[] { "data_modeling:model:read", "data_modeling:record:read" },
        }, Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
