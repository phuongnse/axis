using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant administrator's application packages switch on the tenant's own host, see
/// <c>docs/reference/user-administration.md#application-packages</c>, with real PostgreSQL and Keycloak. Tenant <c>a</c> is served at
/// <c>localhost</c> in a cell and its preview tenant <c>a-preview</c> at <c>a-preview.vn.example.test</c>.
/// </summary>
public sealed class TenantApplicationPackagesTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string AdministratorRole = "tenant-administrator";

    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_checks_run_in_order_and_a_refused_request_writes_nothing()
    {
        await using var factory = await StartAsync(packageCpu: 0);
        using var anonymous = factory.CreateClient();
        using var member = factory.CreateClient().SignedInAs("member");
        using var administrator = factory.CreateClient().SignedInAs("admin");
        using var preview = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://a-preview.vn.example.test") })
            .SignedInAs("admin");
        var version = await VersionAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Get)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Put, """{"on":true}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Get)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Put, "not json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(preview, HttpMethod.Get)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(preview, HttpMethod.Put, "not json")).StatusCode);

        // The body is checked before the quota, so a bad body is a 400 while the quota is 0.
        foreach (var body in new string?[] { null, "not json", "{}", """{"on":"yes"}""", """{"on":null}""" })
        {
            using var bad = await SendAsync(administrator, HttpMethod.Put, body);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        Assert.Equal(version, await VersionAsync());
        Assert.Equal(0, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task While_the_quota_is_0_turning_packages_on_is_the_quota_403_and_turning_them_off_is_accepted()
    {
        await using var factory = await StartAsync(packageCpu: 0);
        using var client = factory.CreateClient().SignedInAs("admin");
        var version = await VersionAsync();

        using var refused = await SendAsync(client, HttpMethod.Put, """{"on":true}""");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("This tenant has reached its package run time quota.", problem.GetProperty("title").GetString());
        Assert.Equal("quota-exceeded", problem.GetProperty("reason").GetString());
        Assert.Equal("packageCpuSecondsPerDay", problem.GetProperty("quota").GetString());
        Assert.Equal(0, problem.GetProperty("limit").GetInt64());
        Assert.Equal(version, await VersionAsync());
        Assert.Equal(0, await CountAsync("platform.audit_records"));
        Assert.False((await ReadAsync(client)).GetProperty("on").GetBoolean());

        using var off = await SendAsync(client, HttpMethod.Put, """{"on":false}""");

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Equal(1, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task With_a_quota_a_PUT_stores_the_switch_raises_the_version_writes_a_record_and_every_host_reads_it()
    {
        await using var factory = await StartAsync(packageCpu: 3600);
        using var client = factory.CreateClient().SignedInAs("admin");
        var version = await VersionAsync();

        var before = await ReadAsync(client);
        using var on = await SendAsync(client, HttpMethod.Put, """{"on":true}""");
        var after = await ReadAsync(client);

        Assert.False(before.GetProperty("on").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.True(after.GetProperty("on").GetBoolean());
        Assert.Equal(version + 1, await VersionAsync());
        Assert.Equal([("tenant application-packages set", "a", "admin", "applicationPackages")], await RecordsAsync());

        var cipher = TenantSecretCipher.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey })
            .Build());
        var catalog = await DirectoryTenantCatalog.LoadAsync(_platform, cipher, CancellationToken);
        Assert.True(catalog.Find("a")!.ApplicationPackages);
        Assert.True(catalog.Find("a-preview")!.ApplicationPackages);

        using var off = await SendAsync(client, HttpMethod.Put, """{"on":false}""");

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.False((await ReadAsync(client)).GetProperty("on").GetBoolean());
        Assert.Equal(version + 2, await VersionAsync());
        Assert.False((await DirectoryTenantCatalog.LoadAsync(_platform, cipher, CancellationToken)).Find("a")!.ApplicationPackages);
    }

    [Fact]
    public async Task With_the_setting_off_turning_packages_on_is_accepted_and_read_back()
    {
        await using var factory = await StartAsync(packageCpu: 3600, applicationPackages: "false");
        using var client = factory.CreateClient().SignedInAs("admin");

        using var on = await SendAsync(client, HttpMethod.Put, """{"on":true}""");

        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.True((await ReadAsync(client)).GetProperty("on").GetBoolean());
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        using var response = await SendAsync(client, HttpMethod.Get);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    // A null body sends none.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri("/api/tenant/application-packages", UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<List<(string Command, string TenantId, string Actor, string Changed)>> RecordsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, actor, array_to_string(changed, ',') FROM platform.audit_records ORDER BY occurred_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var records = new List<(string, string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return records;
    }

    private async Task<long> VersionAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT version FROM platform.tenants WHERE id = 'a'", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<long> CountAsync(string from)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {from}", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    // The built-in default of packageCpuSecondsPerDay is 0. The setting Extensions:ApplicationPackages is unset unless given.
    private async Task<WebApplicationFactory<Program>> StartAsync(long? packageCpu, string? applicationPackages = null)
    {
        var authority = await keycloak.StartAsync();
        _platform = await CellTestPlatform.CreateAsync(database, authority, CancellationToken);

        // The preview tenant of the shared platform is suspended, which the server withholds, and here it serves its host.
        await using (var connection = new NpgsqlConnection(_platform))
        {
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand(
                "UPDATE platform.tenants SET status = 'active', schema_version = @version WHERE id = 'a-preview'", connection);
            command.Parameters.AddWithValue("version", TenantSchemaVersion.Current);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var platform = _platform;
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
            if (packageCpu is { } seconds)
            {
                builder.UseSetting("Quotas:Defaults:packageCpuSecondsPerDay", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (applicationPackages is not null)
            {
                builder.UseSetting("Extensions:ApplicationPackages", applicationPackages);
            }

            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole), new TestIdentity("member", "Member"));
        });
    }
}
