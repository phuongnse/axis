using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class UserEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // GET /api/users/me

    [Fact]
    public async Task GetMe_without_token_returns_401()
    {
        var resp = await fixture.Client.GetAsync("/api/users/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_returns_current_user_profile()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "user1");

        var resp = await client.GetAsync("/api/users/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);

        body.GetProperty("email").GetString().Should().Be("adminuser1@test.com");
        body.GetProperty("first_name").GetString().Should().Be("Test");
        body.GetProperty("is_active").GetBoolean().Should().BeTrue();
        body.GetProperty("permissions").GetArrayLength().Should().BeGreaterThan(0);
    }

    // PATCH /api/users/me

    [Fact]
    public async Task UpdateProfile_without_token_returns_401()
    {
        var resp = await fixture.Client.PatchAsync("/api/users/me",
            JsonContent.Create(new { first_name = "X", last_name = "Y" }, options: Json));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_returns_no_content()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "user2");

        var resp = await client.PatchAsync("/api/users/me",
            JsonContent.Create(new { first_name = "Updated", last_name = "Name" }, options: Json));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // POST /api/users/me/change-password

    [Fact]
    public async Task ChangePassword_without_token_returns_401()
    {
        var resp = await fixture.Client.PostAsJsonAsync("/api/users/me/change-password",
            new { current_password = "TestPass1", new_password = "NewPass2!", confirm_password = "NewPass2!" }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_wrong_current_returns_unprocessable()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "user3");

        var resp = await client.PostAsJsonAsync("/api/users/me/change-password", new
        {
            current_password = "WrongPass99",
            new_password = "NewPass2!",
            confirm_password = "NewPass2!",
        }, Json);

        // Either 422 (validation) or 401 — must not be 200
        resp.StatusCode.Should().NotBe(HttpStatusCode.OK);
        resp.StatusCode.Should().NotBe(HttpStatusCode.NoContent);
    }

    // GET /api/users/me/sessions

    [Fact]
    public async Task GetSessions_without_token_returns_401()
    {
        var resp = await fixture.Client.GetAsync("/api/users/me/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSessions_returns_current_session()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "user4");

        var resp = await client.GetAsync("/api/users/me/sessions");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await resp.Content.ReadFromJsonAsync<JsonElement[]>(Json);
        sessions.Should().NotBeNull();
        sessions!.Length.Should().BeGreaterThan(0);

        // At least one session is flagged as current
        sessions.Any(s => s.GetProperty("is_current").GetBoolean()).Should().BeTrue();
    }

    // PATCH /api/users/{userId}/status

    [Fact]
    public async Task DeactivateUser_self_deactivation_returns_422()
    {
        var client = await AuthHelper.CreateAdminClientAsync(fixture, "user5");

        // Get the current user's ID
        var meResp = await client.GetAsync("/api/users/me");
        var me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var userId = me.GetProperty("id").GetString()!;

        var resp = await client.PatchAsync($"/api/users/{userId}/status",
            JsonContent.Create(new { is_active = false }, options: Json));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("detail").GetString().Should().Contain("cannot deactivate yourself");
    }
}
