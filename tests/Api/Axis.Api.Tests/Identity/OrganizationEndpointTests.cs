using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class OrganizationEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // POST /api/organizations/me/invitations

    [Fact]
    public async Task InviteUser_without_token_returns_401()
    {
        var resp = await fixture.Client.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "someone@test.com", role_id = Guid.NewGuid() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InviteUser_self_invite_returns_422()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "orginv1");

        var resp = await client.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "adminorginv1@test.com", role_id = Guid.NewGuid() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("detail").GetString().Should().Contain("cannot invite yourself");
    }

    [Fact]
    public async Task InviteUser_with_valid_request_returns_ok()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "orginv2");

        // Get org_id from authenticated user, then fetch role scoped to that org
        var meResp = await client.GetAsync("/api/users/me");
        var me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var orgId = Guid.Parse(me.GetProperty("org_id").GetString()!);

        using var scope = fixture.CreateScope();
        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var viewerRole = ctx.Roles.First(r => r.Name == "Viewer" && r.OrganizationId == orgId);

        var resp = await client.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "newuser@test.com", role_id = viewerRole.Id }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("message").GetString().Should().Contain("newuser@test.com");
    }
}
