using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class WorkspaceEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    private static object RegisterPayload(string suffix) => TestRegistrationPayload.Create(suffix);

    [Fact]
    public async Task Register_WhenSameIdempotencyKeyTwice_CreatesOnlyOneWorkspace()
    {
        string idempotencyKey = Guid.NewGuid().ToString();
        object firstPayload = RegisterPayload("idem1a");
        object secondPayload = RegisterPayload("idem1b");

        using HttpRequestMessage first = new(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(firstPayload, options: Json),
        };
        first.Headers.Add("Idempotency-Key", idempotencyKey);

        using HttpRequestMessage second = new(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(secondPayload, options: Json),
        };
        second.Headers.Add("Idempotency-Key", idempotencyKey);

        HttpResponseMessage firstResp = await fixture.Client.SendAsync(first);
        HttpResponseMessage secondResp = await fixture.Client.SendAsync(second);

        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        ctx.Workspaces.Count(o => o.Name == "TestWorkspaceidem1a").Should().Be(1);
        ctx.Workspaces.Count(o => o.Name == "TestWorkspaceidem1b").Should().Be(0);
        ctx.Users.Count(u => u.Email == Email.Create(TestRegistrationPayload.WorkspaceContactEmail("idem1a")).Value)
            .Should().Be(0);
        ctx.Users.Count(u => u.Email == Email.Create(TestRegistrationPayload.WorkspaceContactEmail("idem1b")).Value)
            .Should().Be(0);
    }

    // POST /api/workspaces/me/invitations

    [Fact]
    public async Task InviteUser_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync(
            "/api/workspaces/me/invitations",
            new { email = "someone@test.com", roleId = Guid.NewGuid() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InviteUser_WhenSelfInvite_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "Workspaceinv1");

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/workspaces/me/invitations",
            new { email = "adminWorkspaceinv1@test.com", roleId = Guid.NewGuid() }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("detail").GetString().Should().Contain("cannot invite yourself");
    }

    [Fact]
    public async Task InviteUser_WhenRequestIsValid_ReturnsOk()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "Workspaceinv2");

        // Get WorkspaceId from authenticated user, then fetch role scoped to that Workspace
        HttpResponseMessage meResp = await client.GetAsync("/api/users/me");
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid WorkspaceId = Guid.Parse(me.GetProperty("workspaceId").GetString()!);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Role viewerRole = ctx.Roles.First(r => r.Name == "Viewer" && r.workspaceId == WorkspaceId);

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/workspaces/me/invitations",
            new { email = "newuser@test.com", roleId = viewerRole.Id }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("message").GetString().Should().Contain("newuser@test.com");
    }

    [Fact]
    public async Task GetSlugPreview_WhenworkspaceNameProvided_ReturnsBaseSlug()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync(
            "/api/workspaces/slug-preview?workspaceName=O'Brien%20%26%20Co.");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("slug").GetString().Should().Be("o-brien-co");
    }

    [Fact]
    public async Task Register_WhenTermsVersionsMissing_Returns400()
    {
        object payload = new
        {
            workspaceName = "Acme Corp",
            WorkspaceContactEmail = "noterms@test.com",
        };

        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync("/api/workspaces", payload, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
