using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Data.Profiles;
using Axis.Server.Tests;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>GET /api/users</c> lists the users of the tenant's realm to an administrator. The server reads them from the real
/// <c>axis</c> realm through its <c>axis-users</c> client and the profiles from a real tenant database. The test host
/// treats the role <c>author</c> as the administrator role. The tests never change the <c>axis</c> realm.
/// </summary>
public sealed class UserListTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string HostA = StudioPublishTests.HostA;

    private static readonly string[] _serviceAccounts = ["axis-users", "axis-activation", "purchasing-system", "reporting-system"];

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_administrator_gets_the_realm_users_with_active_for_a_profile_and_invited_without_one()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("user_list_status", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main);
        using var author = factory.CreateClient().SignedInAs("author");

        // Creating the client starts the server, which migrates the tenant database.
        await InsertProfileAsync(main, "administrator");

        using var all = await GetAsync(author, "/api/users?pageSize=50", HostA);

        var items = all.RootElement.GetProperty("items").EnumerateArray().ToList();
        var total = all.RootElement.GetProperty("totalCount").GetInt32();
        Assert.Equal(total, items.Count);
        Assert.DoesNotContain(items, item => _serviceAccounts.Contains(item.GetProperty("id").GetString()));
        var administrator = Assert.Single(items, item => item.GetProperty("id").GetString() == "administrator");
        Assert.Equal("active", administrator.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, administrator.GetProperty("lastSignedInAt").ValueKind);
        Assert.Equal("administrator@axis.local", administrator.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, administrator.GetProperty("provider").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(administrator.GetProperty("displayName").GetString()));
        var auditor = Assert.Single(items, item => item.GetProperty("id").GetString() == "auditor");
        Assert.Equal("invited", auditor.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, auditor.GetProperty("lastSignedInAt").ValueKind);
    }

    [Fact]
    public async Task The_search_matches_an_email_and_two_pages_hold_different_users_with_the_same_total()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("user_list_paging", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main);
        using var author = factory.CreateClient().SignedInAs("author");

        using var found = await GetAsync(author, "/api/users?search=auditor%40axis.local", HostA);
        using var first = await GetAsync(author, "/api/users?page=1&pageSize=2", HostA);
        using var second = await GetAsync(author, "/api/users?page=2&pageSize=2", HostA);

        var match = Assert.Single(found.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("auditor", match.GetProperty("id").GetString());
        Assert.Equal(1, found.RootElement.GetProperty("totalCount").GetInt32());
        var firstIds = Ids(first);
        var secondIds = Ids(second);
        Assert.Equal(2, firstIds.Count);
        Assert.Equal(2, secondIds.Count);
        Assert.Empty(firstIds.Intersect(secondIds));
        var total = first.RootElement.GetProperty("totalCount").GetInt32();
        Assert.Equal(total, second.RootElement.GetProperty("totalCount").GetInt32());
        Assert.True(total > 4);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    public async Task A_page_or_page_size_out_of_range_answers_400(string query)
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("user_list_range", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main);
        using var author = factory.CreateClient().SignedInAs("author");

        using var response = await StudioPublishTests.SendAsync(author, HttpMethod.Get, $"/api/users?{query}", HostA);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_person_without_the_administrator_role_gets_403_and_nobody_signed_in_gets_401()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("user_list_denied", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var nobody = factory.CreateClient();

        using var forbidden = await StudioPublishTests.SendAsync(requester, HttpMethod.Get, "/api/users", HostA);
        using var unauthorized = await StudioPublishTests.SendAsync(nobody, HttpMethod.Get, "/api/users", HostA);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task An_administrator_gets_404_on_the_preview_tenant()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("user_list_preview_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("user_list_preview_preview", CancellationToken);
        await using var factory = await NewFactoryAsync(folder.Path, main, preview);
        using var author = factory.CreateClient().SignedInAs("author");

        using var onMain = await StudioPublishTests.SendAsync(author, HttpMethod.Get, "/api/users", HostA);
        using var onPreview = await StudioPublishTests.SendAsync(author, HttpMethod.Get, "/api/users", StudioPublishTests.PreviewHost);

        Assert.Equal(HttpStatusCode.OK, onMain.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, onPreview.StatusCode);
    }

    // A server whose tenant "a" signs in at the real axis realm and holds the secret of its axis-users client.
    private async Task<Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>> NewFactoryAsync(
        string folder, string main, string? preview = null)
    {
        var authority = await keycloak.StartAsync();
        return StudioPublishTests.NewFactory(folder, [("a", HostA, main)], preview).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tenants:a:Oidc:Authority", authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:UsersClientSecret", KeycloakFixture.UsersClientSecret);
            builder.UseSetting("Users:AdministratorRole", "author");
        });
    }

    private static async Task InsertProfileAsync(string connectionString, string userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await UserProfiles.UpsertAsync(connection, userId, "Administrator", "administrator@axis.local", null, CancellationToken);
    }

    private static List<string> Ids(JsonDocument page) =>
        [.. page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, string host) =>
        await StudioPublishTests.GetJsonAsync(client, path, host);
}
