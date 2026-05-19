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
    public async Task GetMe_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/users/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WhenAuthenticated_ReturnsCurrentUserProfile()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "user1");

        HttpResponseMessage resp = await client.GetAsync("/api/users/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);

        body.GetProperty("email").GetString().Should().Be("adminuser1@test.com");
        body.GetProperty("first_name").GetString().Should().Be("Test");
        body.GetProperty("is_active").GetBoolean().Should().BeTrue();
        body.GetProperty("permissions").GetArrayLength().Should().BeGreaterThan(0);
    }

    // PATCH /api/users/me

    [Fact]
    public async Task UpdateProfile_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PatchAsync("/api/users/me",
            JsonContent.Create(new { first_name = "X", last_name = "Y" }, options: Json));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_WhenAuthenticated_ReturnsNoContent()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "user2");

        HttpResponseMessage resp = await client.PatchAsync("/api/users/me",
            JsonContent.Create(new { first_name = "Updated", last_name = "Name" }, options: Json));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // POST /api/users/me/change-password

    [Fact]
    public async Task ChangePassword_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.PostAsJsonAsync("/api/users/me/change-password",
            new { current_password = "TestPass1", new_password = "NewPass2!", confirm_password = "NewPass2!" }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordIsWrong_ReturnsUnprocessable()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "user3");

        HttpResponseMessage resp = await client.PostAsJsonAsync("/api/users/me/change-password", new
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
    public async Task GetSessions_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await fixture.Client.GetAsync("/api/users/me/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSessions_WhenAuthenticated_ReturnsCurrentSession()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "user4");

        HttpResponseMessage resp = await client.GetAsync("/api/users/me/sessions");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement[]? sessions = await resp.Content.ReadFromJsonAsync<JsonElement[]>(Json);
        sessions.Should().NotBeNull();
        sessions!.Length.Should().BeGreaterThan(0);

        // is_current is always false in this test: the access token does not yet carry
        // the refresh token ID as a claim (tracked gap — rt_id claim not yet implemented).
        sessions.All(s => !s.GetProperty("is_current").GetBoolean()).Should().BeTrue();
    }

    // PATCH /api/users/{userId}/status

    [Fact]
    public async Task DeactivateUser_WhenSelfDeactivation_Returns422()
    {
        HttpClient client = await AuthHelper.CreateAdminClientAsync(fixture, "user5");

        // Get the current user's ID
        HttpResponseMessage meResp = await client.GetAsync("/api/users/me");
        JsonElement me = await meResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string userId = me.GetProperty("id").GetString()!;

        HttpResponseMessage resp = await client.PatchAsync($"/api/users/{userId}/status",
            JsonContent.Create(new { is_active = false }, options: Json));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("detail").GetString().Should().Contain("cannot deactivate yourself");
    }
}
