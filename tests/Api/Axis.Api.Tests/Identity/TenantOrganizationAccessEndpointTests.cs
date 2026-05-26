using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Identity;

/// <summary>
/// E01 F03 — deleted / archived organizations are rejected on tenant API routes (US-009).
/// </summary>
[Collection("Api")]
public sealed class TenantOrganizationAccessEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetModels_WhenOrganizationIsDeleted_Returns403()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgdel1");

        HttpResponseMessage meResp = await client.GetAsync("/api/users/me");
        meResp.EnsureSuccessStatusCode();
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid orgId = Guid.Parse(me.GetProperty("org_id").GetString()!);

        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Organization org = ctx.Organizations.Single(o => o.Id == orgId);
            org.MarkDeleted();
            await ctx.SaveChangesAsync();
        }

        HttpResponseMessage resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetModels_WhenOrganizationIsArchived_Returns403()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgarc1");

        HttpResponseMessage meResp = await client.GetAsync("/api/users/me");
        meResp.EnsureSuccessStatusCode();
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid orgId = Guid.Parse(me.GetProperty("org_id").GetString()!);

        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Organization org = ctx.Organizations.Single(o => o.Id == orgId);
            org.Archive();
            await ctx.SaveChangesAsync();
        }

        HttpResponseMessage resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetModels_WhenOrganizationIsDeletionScheduled_StillAllowsAccess()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgsched1");

        HttpResponseMessage settingsResp = await client.GetAsync("/api/organizations/current/settings");
        settingsResp.EnsureSuccessStatusCode();
        JsonElement settings = await settingsResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string orgName = settings.GetProperty("name").GetString()!;

        HttpResponseMessage scheduleResp = await client.PostAsJsonAsync(
            "/api/organizations/current/deletion",
            new { confirmation_name = orgName },
            Json);
        scheduleResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage resp = await client.GetAsync("/api/models");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
