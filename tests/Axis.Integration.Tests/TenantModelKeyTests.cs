using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant administrator's model key on the tenant's own host, see <c>docs/reference/user-administration.md#the-tenants-model-key</c>,
/// with real PostgreSQL and Keycloak. Tenant <c>a</c> is served at <c>localhost</c> in a cell.
/// </summary>
public sealed class TenantModelKeyTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string AdministratorRole = "tenant-administrator";
    private const string FirstKey = "sk-first-key-1234567890";
    private const string SecondKey = "sk-second-key-abcdefghij";

    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_the_setting_off_every_route_is_a_403_to_an_administrator_and_nothing_is_written()
    {
        await using var factory = await StartAsync(allow: false);
        using var anonymous = factory.CreateClient();
        using var member = factory.CreateClient().SignedInAs("member");
        using var administrator = factory.CreateClient().SignedInAs("admin");

        var statuses = new List<HttpStatusCode>();
        foreach (var client in new[] { administrator, member })
        {
            statuses.Add((await SendAsync(client, HttpMethod.Get)).StatusCode);
            statuses.Add((await SendAsync(client, HttpMethod.Put, FirstKey)).StatusCode);
            statuses.Add((await SendAsync(client, HttpMethod.Delete)).StatusCode);
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Forbidden, status));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Get)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Put, FirstKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Delete)).StatusCode);
        Assert.Equal(0, await CountAsync("platform.audit_records"));
        Assert.Equal(0, await CountAsync("platform.tenants WHERE model_key IS NOT NULL"));
    }

    [Fact]
    public async Task A_member_and_a_caller_with_no_session_are_refused_with_the_setting_on()
    {
        await using var factory = await StartAsync(allow: true);
        using var anonymous = factory.CreateClient();
        using var member = factory.CreateClient().SignedInAs("member");

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Get)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Put, FirstKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Get)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Put, FirstKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Delete)).StatusCode);
        Assert.Equal(0, await CountAsync("platform.audit_records"));
        Assert.Equal(0, await CountAsync("platform.tenants WHERE model_key IS NOT NULL"));
    }

    [Fact]
    public async Task Set_change_and_remove_each_write_a_record_that_names_modelKey_and_holds_no_key()
    {
        await using var factory = await StartAsync(allow: true);
        using var client = factory.CreateClient().SignedInAs("admin");
        var version = await VersionAsync();

        using var first = await SendAsync(client, HttpMethod.Put, $"  {FirstKey}  ");
        using var second = await SendAsync(client, HttpMethod.Put, SecondKey);
        using var removed = await SendAsync(client, HttpMethod.Delete);
        using var removedAgain = await SendAsync(client, HttpMethod.Delete);
        using var blank = await SendAsync(client, HttpMethod.Put, "   ");
        using var missing = await SendAsync(client, HttpMethod.Put, null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(
            [
                ("tenant model-key set", "a", "admin", "modelKey"),
                ("tenant model-key set", "a", "admin", "modelKey"),
                ("tenant model-key remove", "a", "admin", "modelKey"),
            ],
            await RecordsAsync());
        Assert.Equal(version + 3, await VersionAsync());
        Assert.Equal(0, await CountAsync("platform.tenants WHERE model_key IS NOT NULL"));
        foreach (var key in new[] { FirstKey, SecondKey })
        {
            Assert.Equal(0, await CountAsync($"platform.audit_records t WHERE t::text LIKE '%{key}%'"));
        }
    }

    [Fact]
    public async Task Get_answers_only_the_ending_and_the_stored_key_decrypts_for_that_tenant_and_field_only()
    {
        await using var factory = await StartAsync(allow: true);
        using var client = factory.CreateClient().SignedInAs("admin");

        var before = await ReadAsync(client);
        using var set = await SendAsync(client, HttpMethod.Put, FirstKey);
        var after = await ReadAsync(client);
        using var shortKey = await SendAsync(client, HttpMethod.Put, "abcd");
        var shortRead = await ReadAsync(client);
        using var changed = await SendAsync(client, HttpMethod.Put, SecondKey);
        var changedBody = await (await SendAsync(client, HttpMethod.Get)).Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.False(before.GetProperty("set").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("ending").ValueKind);
        Assert.True(after.GetProperty("set").GetBoolean());
        Assert.Equal(FirstKey[^4..], after.GetProperty("ending").GetString());
        Assert.True(shortRead.GetProperty("set").GetBoolean());
        Assert.Equal(JsonValueKind.Null, shortRead.GetProperty("ending").ValueKind);
        Assert.DoesNotContain(SecondKey, changedBody, StringComparison.Ordinal);
        Assert.Contains(SecondKey[^4..], changedBody, StringComparison.Ordinal);

        var cipher = TenantSecretCipher.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey })
            .Build());
        var stored = await StoredAsync();
        Assert.Equal(SecondKey, cipher.Decrypt("a", TenantSecretFields.ModelKey, stored).Reveal());
        Assert.Throws<InvalidOperationException>(() => cipher.Decrypt("b", TenantSecretFields.ModelKey, stored));
        Assert.Throws<InvalidOperationException>(() => cipher.Decrypt("a", TenantSecretFields.SmtpPassword, stored));
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        using var response = await SendAsync(client, HttpMethod.Get);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    // A null key sends no body.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string? key = null)
    {
        using var request = new HttpRequestMessage(method, new Uri("/api/tenant/model-key", UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (key is not null)
        {
            request.Content = JsonContent.Create(new { key });
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

    private async Task<byte[]> StoredAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT model_key FROM platform.tenants WHERE id = 'a'", connection);
        return (byte[])(await command.ExecuteScalarAsync(CancellationToken))!;
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

    private async Task<WebApplicationFactory<Program>> StartAsync(bool allow)
    {
        var authority = await keycloak.StartAsync();
        _platform = await CellTestPlatform.CreateAsync(database, authority, CancellationToken);

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
            builder.UseSetting("Ai:AllowTenantKeys", allow ? "true" : "false");
            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole), new TestIdentity("member", "Member"));
        });
    }
}
