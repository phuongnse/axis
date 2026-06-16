using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Workspaces;

[Collection("Api")]
public sealed class WorkspaceIsolationEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetModel_WhenModelBelongsToAnotherWorkspace_Returns404()
    {
        HttpClient workspaceA = await AuthHelper.CreateAdminClientAsync(fixture, "iso-a");
        HttpClient workspaceB = await AuthHelper.CreateAdminClientAsync(fixture, "iso-b");

        HttpResponseMessage createResp = await workspaceA.PostAsJsonAsync("/api/models", new
        {
            name = "WorkspaceAOnly",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string modelId = created.GetProperty("id").GetString()!;

        HttpResponseMessage crossRead = await workspaceB.GetAsync($"/api/models/{modelId}");

        crossRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetModels_WhenAnotherWorkspaceCreatedModels_ReturnsEmptyList()
    {
        HttpClient workspaceA = await AuthHelper.CreateAdminClientAsync(fixture, "iso-c");
        HttpClient workspaceB = await AuthHelper.CreateAdminClientAsync(fixture, "iso-d");

        HttpResponseMessage createResp = await workspaceA.PostAsJsonAsync("/api/models", new
        {
            name = "PrivateModel",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage listResp = await workspaceB.GetAsync("/api/models");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await listResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetModels_WhenWorkspaceIsArchived_Returns403()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "iso-archived");
        Guid workspaceId = await GetworkspaceIdForEmailAsync("adminiso-archived@test.com");
        await SetWorkspaceStatusAsync(workspaceId, WorkspaceStatus.Archived);

        HttpResponseMessage resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> GetworkspaceIdForEmailAsync(string emailAddress)
    {
        Email email = Email.Create(emailAddress).Value!;
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        Guid workspaceId = await (
            from membership in db.WorkspaceMemberships.AsNoTracking()
            join workspace in db.Workspaces.AsNoTracking() on membership.workspaceId equals workspace.Id
            where membership.UserId == user.Id && workspace.Type == WorkspaceType.Team
            select workspace.Id)
            .SingleAsync();
        return workspaceId;
    }

    private async Task SetWorkspaceStatusAsync(Guid workspaceId, WorkspaceStatus status)
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Workspace Workspace = await db.Workspaces.SingleAsync(o => o.Id == workspaceId);

        switch (status)
        {
            case WorkspaceStatus.Archived:
                if (Workspace.Status == WorkspaceStatus.Provisioning)
                    Workspace.CompleteProvisioning();
                Workspace.Archive();
                break;
            default:
                throw new NotSupportedException($"Test helper does not set status {status}.");
        }

        await db.SaveChangesAsync();
    }
}
