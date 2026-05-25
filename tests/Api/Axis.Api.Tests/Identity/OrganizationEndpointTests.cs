using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class OrganizationEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    private static object RegisterPayload(string suffix) => new
    {
        org_name = $"TestOrg{suffix}",
        admin_first_name = "Test",
        admin_last_name = "Admin",
        admin_email = $"admin{suffix}@test.com",
        password = "TestPass1",
        password_confirmation = "TestPass1",
    };

    [Fact]
    public async Task Register_WhenSameIdempotencyKeyTwice_CreatesOnlyOneOrganization()
    {
        string idempotencyKey = Guid.NewGuid().ToString();
        object payload = RegisterPayload("idem1");

        using HttpRequestMessage first = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        first.Headers.Add("Idempotency-Key", idempotencyKey);

        using HttpRequestMessage second = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        second.Headers.Add("Idempotency-Key", idempotencyKey);

        HttpResponseMessage firstResp = await fixture.Client.SendAsync(first);
        HttpResponseMessage secondResp = await fixture.Client.SendAsync(second);

        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        int orgCount = ctx.Organizations.Count(o => o.Name == "TestOrgidem1");
        orgCount.Should().Be(1);
    }

    // POST /api/organizations/me/invitations

    [Fact]
    public async Task InviteUser_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "someone@test.com", role_id = Guid.NewGuid() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InviteUser_WhenSelfInvite_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orginv1");

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "adminorginv1@test.com", role_id = Guid.NewGuid() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("detail").GetString().Should().Contain("cannot invite yourself");
    }

    [Fact]
    public async Task InviteUser_WhenRequestIsValid_ReturnsOk()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orginv2");

        // Get org_id from authenticated user, then fetch role scoped to that org
        HttpResponseMessage meResp = await client.GetAsync("/api/users/me");
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid orgId = Guid.Parse(me.GetProperty("org_id").GetString()!);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Role viewerRole = ctx.Roles.First(r => r.Name == "Viewer" && r.OrganizationId == orgId);

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "newuser@test.com", role_id = viewerRole.Id }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("message").GetString().Should().Contain("newuser@test.com");
    }
}
