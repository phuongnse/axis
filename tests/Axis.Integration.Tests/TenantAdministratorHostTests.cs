using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant administrator's request for a host on the tenant's own host, see
/// <c>docs/reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator</c>, with real PostgreSQL and Keycloak. Tenant <c>a</c> is served at
/// <c>localhost</c> in a cell and its preview tenant <c>a-preview</c> at <c>a-preview.vn.example.test</c>. The verification by the publisher is
/// tested in <see cref="TenantHostVerificationTests"/>.
/// </summary>
public sealed class TenantAdministratorHostTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string AdministratorRole = "tenant-administrator";

    private string _platform = null!;
    private Uri _authority = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_administrator_asks_for_a_host_and_a_repeat_answers_the_same_token_and_the_list_shows_it_as_pending()
    {
        await using var factory = await StartAsync();
        using var client = factory.CreateClient().SignedInAs("admin");

        using var first = await PostAsync(client, """{"host":" Shop.Acme.Test "}""");
        using var repeat = await PostAsync(client, """{"host":"shop.acme.test"}""");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var token = created.GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal("shop.acme.test", created.GetProperty("host").GetString());
        Assert.Equal("pending", created.GetProperty("state").GetString());
        Assert.Equal("_axis-verify.shop.acme.test", created.GetProperty("txt").GetProperty("name").GetString());
        Assert.Equal(token, created.GetProperty("txt").GetProperty("value").GetString());
        Assert.Equal("shop.acme.test", created.GetProperty("cname").GetProperty("name").GetString());
        Assert.Equal("a.vn.example.test", created.GetProperty("cname").GetProperty("target").GetString());
        Assert.True(created.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(6));

        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var again = await repeat.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(token, again.GetProperty("token").GetString());
        Assert.Equal(["admin"], await ActorsAsync());

        using var list = await client.GetAsync(new Uri("/api/tenant/hosts", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal("shop.acme.test", item.GetProperty("host").GetString());
        Assert.Equal("pending", item.GetProperty("state").GetString());
        Assert.Equal(again.GetProperty("expiresAt").GetDateTimeOffset(), item.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.False(item.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task A_request_without_a_session_the_role_or_a_cell_tenant_is_refused_and_stores_nothing()
    {
        await using var factory = await StartAsync();
        using var anonymous = factory.CreateClient();
        using var member = factory.CreateClient().SignedInAs("member");
        using var preview = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://a-preview.vn.example.test") })
            .SignedInAs("admin");
        await using var outsideCell = StartOutsideCell();
        using var outside = outsideCell.CreateClient().SignedInAs("admin");

        const string body = """{"host":"shop.acme.test"}""";
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(member, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(member)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(preview, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(preview)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(outside, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(outside)).StatusCode);
        Assert.Empty(await ActorsAsync());
    }

    [Fact]
    public async Task A_bad_host_is_a_400_and_a_held_host_or_one_held_back_after_a_deletion_is_a_409_that_names_no_tenant()
    {
        await using var factory = await StartAsync();
        using var client = factory.CreateClient().SignedInAs("admin");
        await ExecuteAsync(
            "INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone', now() - interval '10 days', 'test', '{gone.vn.example.test}')");

        using var bad = await PostAsync(client, """{"host":"localhost"}""");
        using var held = await PostAsync(client, """{"host":"b2.vn.example.test"}""");
        using var heldBack = await PostAsync(client, """{"host":"gone.vn.example.test"}""");

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        var heldBody = await held.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("\"b\"", heldBody, StringComparison.Ordinal);
        Assert.DoesNotContain("b2.vn", heldBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, heldBack.StatusCode);
        var heldBackBody = await heldBack.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("gone", heldBackBody, StringComparison.Ordinal);
        Assert.Empty(await ActorsAsync());
    }

    [Fact]
    public async Task In_a_read_only_tenant_the_add_is_refused_and_the_list_still_answers()
    {
        await using var factory = await StartAsync(tenantStatus: "readOnly");
        using var client = factory.CreateClient().SignedInAs("admin");

        using var add = await PostAsync(client, """{"host":"shop.acme.test"}""");
        using var list = await GetAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        var problem = await add.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("tenant-read-only", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Empty((await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items").EnumerateArray());
        Assert.Empty(await ActorsAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/tenant/hosts", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client) =>
        client.GetAsync(new Uri("/api/tenant/hosts", UriKind.Relative), CancellationToken);

    private async Task<List<string>> ActorsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT actor FROM platform.tenant_host_requests ORDER BY requested_at", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var actors = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            actors.Add(reader.GetString(0));
        }

        return actors;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<WebApplicationFactory<Program>> StartAsync(string tenantStatus = "active")
    {
        _authority = await keycloak.StartAsync();
        _platform = await CellTestPlatform.CreateAsync(database, _authority, CancellationToken);

        // The directory loads at start, so the status is set before the first request. The preview tenant of the shared platform is
        // suspended, which the server withholds, and here it serves its host.
        await ExecuteAsync($"UPDATE platform.tenants SET status = '{tenantStatus}' WHERE id = 'a'");
        await using (var connection = new NpgsqlConnection(_platform))
        {
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand(
                "UPDATE platform.tenants SET status = 'active', schema_version = @version WHERE id = 'a-preview'", connection);
            command.Parameters.AddWithValue("version", TenantSchemaVersion.Current);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var platform = _platform;
        var authority = _authority;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:ConsoleHost", "console.test");
            builder.UseSetting("Cell:OperatorRealm", "operator");
            builder.UseSetting("Cell:OperatorRole", "cell-operator");
            builder.UseSetting("IdentityProvider:Url", authority.ToString());
            builder.UseSetting("Users:AdministratorRole", AdministratorRole);
            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole), new TestIdentity("member", "Member"));
        });
    }

    // The same platform without the Cell settings, so the server is no cell and tenant a is served at localhost.
    private WebApplicationFactory<Program> StartOutsideCell()
    {
        var platform = _platform;
        var authority = _authority;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("IdentityProvider:Url", authority.ToString());
            builder.UseSetting("Users:AdministratorRole", AdministratorRole);
            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole), new TestIdentity("member", "Member"));
        });
    }
}
