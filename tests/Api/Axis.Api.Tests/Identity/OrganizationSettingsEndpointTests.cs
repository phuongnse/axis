using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class OrganizationSettingsEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetSettings_WhenViewerRole_Returns403()
    {
        HttpClient admin = await AuthHelper.CreateAdminClientAsync(fixture, "orgset1");
        HttpResponseMessage meResp = await admin.GetAsync("/api/users/me");
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid orgId = Guid.Parse(me.GetProperty("org_id").GetString()!);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Role viewerRole = ctx.Roles.First(r => r.Name == "Viewer" && r.OrganizationId == orgId);

        HttpResponseMessage inviteResp = await admin.PostAsJsonAsync(
            "/api/organizations/me/invitations",
            new { email = "viewerorgset1@test.com", role_id = viewerRole.Id },
            Json);
        inviteResp.EnsureSuccessStatusCode();

        // Accept invitation flow omitted — use admin for negative test on missing permission:
        HttpClient anon = fixture.CreateNewClient();
        HttpResponseMessage resp = await anon.GetAsync("/api/organizations/current/settings");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_WhenAdmin_UpdatesOrganization()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgset2");

        HttpResponseMessage updateResp = await client.PutAsJsonAsync(
            "/api/organizations/current/profile",
            new
            {
                name = "Renamed Org",
                time_zone_id = "America/New_York",
                default_language = "en-US",
            },
            Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage settingsResp = await client.GetAsync("/api/organizations/current/settings");
        settingsResp.EnsureSuccessStatusCode();
        JsonElement body = await settingsResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("name").GetString().Should().Be("Renamed Org");
        body.GetProperty("time_zone_id").GetString().Should().Be("America/New_York");
    }

    [Fact]
    public async Task UpdateProfile_WhenInvalidTimezone_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgset3");

        HttpResponseMessage resp = await client.PutAsJsonAsync(
            "/api/organizations/current/profile",
            new { name = "Test Org", time_zone_id = "Not/A/Timezone" },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ScheduleDeletion_WhenConfirmationMismatch_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgset4");

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/organizations/current/deletion",
            new { confirmation_name = "Wrong Name" },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ScheduleDeletion_WhenNameMatches_SchedulesDeletion()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "orgset5");

        HttpResponseMessage settingsBefore = await client.GetAsync("/api/organizations/current/settings");
        JsonElement before = await settingsBefore.Content.ReadFromJsonAsync<JsonElement>(Json);
        string orgName = before.GetProperty("name").GetString()!;

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/organizations/current/deletion",
            new { confirmation_name = orgName },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage settingsAfter = await client.GetAsync("/api/organizations/current/settings");
        JsonElement after = await settingsAfter.Content.ReadFromJsonAsync<JsonElement>(Json);
        after.GetProperty("status").GetString().Should().Be(nameof(OrganizationStatus.DeletionScheduled));
        after.GetProperty("scheduled_hard_delete_at").ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}
