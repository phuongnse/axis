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
public class TeamAccountSettingsEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetSettings_WhenViewerRole_Returns403()
    {
        const string viewerEmail = "viewerteamset1@test.com";
        const string viewerPassword = "viewer account passphrase";

        HttpClient admin = await AuthHelper.CreateAdminClientAsync(fixture, "teamset1");
        HttpResponseMessage meResp = await admin.GetAsync("/api/users/me");
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid teamAccountId = Guid.Parse(me.GetProperty("teamAccountId").GetString()!);

        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Role viewerRole = ctx.Roles.First(r => r.Name == "Viewer" && r.TeamAccountId == teamAccountId);

            HttpResponseMessage inviteResp = await admin.PostAsJsonAsync(
                "/api/team-accounts/me/invitations",
                new { email = viewerEmail, roleId = viewerRole.Id },
                Json);
            inviteResp.EnsureSuccessStatusCode();

            // Email is a value object — avoid .Value in LINQ (EF cannot translate it).
            string token = ctx.Invitations
                .Where(i => i.TeamAccountId == teamAccountId)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => i.Token)
                .First();

            HttpClient acceptClient = fixture.CreateNewClient();
            HttpResponseMessage acceptResp = await acceptClient.PostAsJsonAsync(
                $"/api/invitations/{token}/accept",
                new
                {
                    firstName = "View",
                    lastName = "Only",
                    password = viewerPassword,
                },
                Json);
            acceptResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        HttpClient viewerClient = fixture.CreateNewClient();
        string accessToken = await AuthHelper.CompletePkceFlowAsync(viewerClient, viewerEmail, viewerPassword);
        viewerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage resp = await viewerClient.GetAsync("/api/team-accounts/current/settings");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateProfile_WhenLogoBase64IsMalformed_Returns400()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "teamset3c");

        HttpResponseMessage resp = await client.PutAsJsonAsync(
            "/api/team-accounts/current/profile",
            new { name = "Test Team Account", logoBase64 = "not-valid-base64!!!" },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_WhenAdmin_UpdatesTeamAccount()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "teamset2");

        HttpResponseMessage updateResp = await client.PutAsJsonAsync(
            "/api/team-accounts/current/profile",
            new
            {
                name = "Renamed Team Account",
                timeZoneId = "America/New_York",
                defaultLanguage = "en-US",
            },
            Json);

        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage settingsResp = await client.GetAsync("/api/team-accounts/current/settings");
        settingsResp.EnsureSuccessStatusCode();
        JsonElement body = await settingsResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("name").GetString().Should().Be("Renamed Team Account");
        body.GetProperty("timeZoneId").GetString().Should().Be("America/New_York");
    }

    [Fact]
    public async Task UpdateProfile_WhenInvalidLanguage_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "teamset3b");

        HttpResponseMessage resp = await client.PutAsJsonAsync(
            "/api/team-accounts/current/profile",
            new { name = "Test Team Account", defaultLanguage = "english" },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task UpdateProfile_WhenInvalidTimezone_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "teamset3");

        HttpResponseMessage resp = await client.PutAsJsonAsync(
            "/api/team-accounts/current/profile",
            new { name = "Test Team Account", timeZoneId = "Not/A/Timezone" },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ScheduleDeletion_WhenConfirmationMismatch_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "teamset4");

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/team-accounts/current/deletion",
            new { confirmationName = "Wrong Name" },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ScheduleDeletion_WhenNameMatches_SchedulesDeletion()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "teamset5");

        HttpResponseMessage settingsBefore = await client.GetAsync("/api/team-accounts/current/settings");
        JsonElement before = await settingsBefore.Content.ReadFromJsonAsync<JsonElement>(Json);
        string teamAccountName = before.GetProperty("name").GetString()!;

        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/team-accounts/current/deletion",
            new { confirmationName = teamAccountName },
            Json);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage settingsAfter = await client.GetAsync("/api/team-accounts/current/settings");
        JsonElement after = await settingsAfter.Content.ReadFromJsonAsync<JsonElement>(Json);
        after.GetProperty("status").GetString().Should().Be(nameof(TeamAccountStatus.DeletionScheduled));
        after.GetProperty("scheduledHardDeleteAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}
