using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <see cref="TenantPurge"/> on real PostgreSQL and Keycloak. The eight steps remove a tenant and its preview tenant with their databases, roles,
/// realm, Studio folders, keys and every row of the <c>platform</c> tables that holds a <c>tenant_id</c>, except the jobs and the audit records.
/// Each purged tenant gets a tombstone with its id, time, actor and hosts. A purge that stopped after the <c>database</c> step finishes when it runs
/// again and a second tenant stays whole. A job without an actor in its input is refused before it removes a host.
/// </summary>
public sealed class AdminTenantPurgeTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Input = """{"actor":"operator-1"}""";

    private static readonly string[] _stepNames = ["hosts", "database", "realm", "files", "studio", "keys", "rows", "directory"];

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private readonly string _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        File.WriteAllText(_cellKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
    }

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
        File.Delete(_cellKeyFile);
    }

    [Fact]
    public async Task A_purge_removes_a_tenant_and_its_preview_tenant_and_leaves_a_tombstone_for_each_and_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var (id, preview) = await CreateWithPreviewAsync(scenario, studio);
        var hosts = await scenario.HostsAsync(id);
        var previewHosts = await scenario.HostsAsync(preview);
        Assert.NotEmpty(hosts);
        Assert.NotEmpty(previewHosts);
        var auditBefore = await CountAuditRecordsAsync(scenario, id, preview);
        Assert.NotEqual(0, auditBefore);
        foreach (var tenant in new[] { id, preview })
        {
            Assert.True(await scenario.DatabaseExistsAsync(tenant));
            Assert.Equal(2, await scenario.CountRolesAsync(tenant));
            Assert.NotEmpty(await scenario.KeysAsync(tenant));
            Assert.True(Directory.Exists(Path.Combine(studio.Path, tenant)));
            Assert.True(LibGit2Sharp.Repository.IsValid(RevisionsOf(studio, tenant)));
        }

        Assert.True(await RealmExistsAsync(id));
        Assert.True(await BackupKeyExistsAsync(scenario, id));

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await RunAsync(PurgeFor(scenario, studio), id, _stepNames);

        foreach (var tenant in new[] { id, preview })
        {
            await AssertGoneAsync(scenario, studio, tenant);
        }

        Assert.False(await RealmExistsAsync(id));
        await using var context = NewContext(scenario);
        var tombstones = await context.DeletedTenants.AsNoTracking().OrderBy(row => row.Id).ToListAsync(CancellationToken);
        Assert.Equal(new[] { id, preview }.Order(StringComparer.Ordinal), tombstones.Select(row => row.Id));
        foreach (var tombstone in tombstones)
        {
            Assert.Equal("operator-1", tombstone.Actor);
            Assert.InRange(tombstone.DeletedAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.Equal(tombstone.Id == id ? hosts : previewHosts, tombstone.Hosts);
        }

        Assert.Equal(auditBefore, await CountAuditRecordsAsync(scenario, id, preview));
    }

    [Fact]
    public async Task A_purge_leaves_no_row_of_the_tenant_in_any_platform_table_but_the_jobs_and_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var id = await CreateTenantAsync(scenario, studio);
        await SeedAsync(scenario, id);
        var tables = await TablesWithTenantIdAsync(scenario);
        Assert.Contains("tenant_hosts", tables);
        Assert.Contains("tenant_quotas", tables);
        Assert.DoesNotContain("jobs", tables);
        Assert.DoesNotContain("audit_records", tables);
        foreach (var table in tables)
        {
            Assert.True(await CountRowsAsync(scenario, table, id) > 0, table);
        }

        await RunAsync(PurgeFor(scenario, studio), id, _stepNames);

        foreach (var table in tables)
        {
            Assert.Equal(0L, await CountRowsAsync(scenario, table, id));
        }

        Assert.Equal(1L, await CountRowsAsync(scenario, "jobs", id));
        Assert.NotEqual(0L, await CountRowsAsync(scenario, "audit_records", id));
    }

    [Fact]
    public async Task A_purge_stopped_after_the_database_step_finishes_when_it_runs_again_and_a_second_tenant_stays_whole()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var id = await CreateTenantAsync(scenario, studio);
        var other = await CreateTenantAsync(scenario, studio);
        var otherHosts = await scenario.HostsAsync(other);
        var otherKeys = (await scenario.KeysAsync(other)).Count;
        Assert.NotEqual(0, otherKeys);
        var purge = PurgeFor(scenario, studio);

        await RunAsync(purge, id, _stepNames[..2]);
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.True(await RowExistsAsync(scenario, id));

        // The stop left the realm, the folder, the keys and the row. A rerun of the finished steps does nothing and the rest finishes the job.
        await RunAsync(purge, id, _stepNames[1..]);

        await AssertGoneAsync(scenario, studio, id);
        Assert.False(await RealmExistsAsync(id));
        Assert.True(await RowExistsAsync(scenario, other));
        Assert.Equal(otherHosts, await scenario.HostsAsync(other));
        Assert.Equal(otherKeys, (await scenario.KeysAsync(other)).Count);
        Assert.True(await BackupKeyExistsAsync(scenario, other));
        Assert.True(await scenario.DatabaseExistsAsync(other));
        Assert.Equal(2, await scenario.CountRolesAsync(other));
        Assert.True(await RealmExistsAsync(other));
        Assert.True(Directory.Exists(Path.Combine(studio.Path, other)));
        Assert.True(LibGit2Sharp.Repository.IsValid(RevisionsOf(studio, other)));
        await using var context = NewContext(scenario);
        Assert.Equal([id], await context.DeletedTenants.AsNoTracking().Select(row => row.Id).ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task The_hosts_step_clears_the_redirect_addresses_and_a_job_without_an_actor_is_refused_before_it_removes_a_host()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var id = await CreateTenantAsync(scenario, studio);
        var hosts = await scenario.HostsAsync(id);
        Assert.NotEmpty(await RedirectAddressesAsync(id));
        var hostsStep = PurgeFor(scenario, studio).Steps().Single(step => step.Name == "hosts");

        foreach (var input in new string?[] { null, "", "  ", "not json", "{}", """{"actor":""}""", """{"actor":" "}""", "[]" })
        {
            var refusal = await hostsStep.RunAsync(Guid.NewGuid(), id, input, CancellationToken);
            Assert.NotNull(refusal);
            Assert.Equal(hosts, await scenario.HostsAsync(id));
            await using var context = NewContext(scenario);
            Assert.False(await context.DeletedTenants.AnyAsync(CancellationToken));
        }

        Assert.NotEmpty(await RedirectAddressesAsync(id));

        Assert.Null(await hostsStep.RunAsync(Guid.NewGuid(), id, Input, CancellationToken));

        Assert.Empty(await scenario.HostsAsync(id));
        Assert.Empty(await RedirectAddressesAsync(id));
        Assert.True(await RowExistsAsync(scenario, id));
        await using var after = NewContext(scenario);
        var tombstone = await after.DeletedTenants.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal(hosts, tombstone.Hosts);

        // A rerun after the hosts are gone keeps the first tombstone.
        Assert.Null(await hostsStep.RunAsync(Guid.NewGuid(), id, """{"actor":"operator-2"}""", CancellationToken));
        var kept = await after.DeletedTenants.AsNoTracking().SingleAsync(CancellationToken);
        Assert.Equal("operator-1", kept.Actor);
        Assert.Equal(hosts, kept.Hosts);
    }

    [Fact]
    public void The_steps_are_named_in_the_order_of_the_purge()
    {
        var purge = new TenantPurge(
            new NpgsqlConnectionStringBuilder(),
            "",
            AdminScenario.Cipher,
            new HttpClient(_handler, disposeHandler: false),
            new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret),
            null);

        Assert.Equal(_stepNames, purge.Steps().Select(step => step.Name));
    }

    private static async Task RunAsync(TenantPurge purge, string id, IEnumerable<string> names)
    {
        var steps = purge.Steps().ToDictionary(step => step.Name);
        foreach (var name in names)
        {
            Assert.Null(await steps[name].RunAsync(Guid.NewGuid(), id, Input, CancellationToken));
        }
    }

    private async Task<string> CreateTenantAsync(AdminScenario scenario, TemporaryFolder studio)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(
            AdminScenario.CreateArgs(id, $"{id}.example.test", $"extra-{id}.example.test"), identityProvider: _handler, extra: Settings(studio));
        Assert.Equal(0, created.Exit);
        await WriteStudioFileAsync(studio, id);
        return id;
    }

    private async Task<(string Id, string Preview)> CreateWithPreviewAsync(AdminScenario scenario, TemporaryFolder studio)
    {
        var id = await CreateTenantAsync(scenario, studio);
        var preview = scenario.NewTenantId();
        var created = await scenario.RunAsync(
            AdminScenario.PreviewArgs(preview, id, $"{preview}.example.test"), identityProvider: _handler, extra: Settings(studio));
        Assert.Equal(0, created.Exit);
        await WriteStudioFileAsync(studio, preview);
        return (id, preview);
    }

    // An application folder with a real revisions repository in the tenant's .revisions folder.
    private static async Task WriteStudioFileAsync(TemporaryFolder studio, string id) =>
        _ = await TenantRevisions.BuildAsync(Path.Combine(studio.Path, id), "app-a", CancellationToken);

    private static string RevisionsOf(TemporaryFolder studio, string id) => Path.Combine(studio.Path, id, ".revisions", "app-a.git");

    // One row in each platform table that a purge removes by hand, and a job row, which stays.
    private static async Task SeedAsync(AdminScenario scenario, string id)
    {
        string[] statements =
        [
            $"INSERT INTO platform.tenant_quotas (tenant_id, quota, value) VALUES ('{id}', 'users', 5)",
            $"INSERT INTO platform.tenant_leases (lease_id, tenant_id, quota, holder, expires_at) VALUES (gen_random_uuid(), '{id}', 'work-items', 'host-1', now() + interval '1 hour')",
            $"INSERT INTO platform.tenant_usage (tenant_id, day) VALUES ('{id}', current_date)",
            $"INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at) VALUES ('{id}', 1, 2, now())",
            $"INSERT INTO platform.tenant_daily_counters (tenant_id, day, counter, value) VALUES ('{id}', current_date, 'emails', 3)",
            $"INSERT INTO platform.tenant_backups (tenant_id, last_success_at) VALUES ('{id}', now())",
            $"INSERT INTO platform.tenant_host_requests (tenant_id, host, token, state, requested_at, expires_at, actor) VALUES ('{id}', 'own-{id}.example.test', 'token', 'pending', now(), now() + interval '1 day', 'operator-1')",
            $"INSERT INTO platform.tenant_site_legal_links (tenant_id, site_path, privacy_url) VALUES ('{id}', 'sales', 'https://example.test/privacy')",
            $"INSERT INTO platform.tenant_platform_providers (tenant_id, name, type, role_values) VALUES ('{id}', 'corp', 'oidc', '[]')",
            $"INSERT INTO platform.tenant_bindings (tenant_id, name, kind, url) VALUES ('{id}', 'ReportsUrl', 'url', 'https://reports.example.test')",
            $"INSERT INTO platform.jobs (id, kind, tenant_id, state, steps, actor) VALUES (gen_random_uuid(), 'suspend', '{id}', 'succeeded', '[]', 'operator-1')",
        ];
        foreach (var statement in statements)
        {
            await PostgreSqlServer.AdminScalarAsync(scenario.Platform, statement, CancellationToken);
        }
    }

    private static async Task<List<string>> TablesWithTenantIdAsync(AdminScenario scenario)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name FROM information_schema.columns
            WHERE table_schema = 'platform' AND column_name = 'tenant_id' AND table_name NOT IN ('jobs', 'audit_records')
            ORDER BY table_name
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<long> CountRowsAsync(AdminScenario scenario, string table, string id)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"""SELECT count(*) FROM platform."{table}" WHERE tenant_id = @id""", connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static async Task<int> CountAuditRecordsAsync(AdminScenario scenario, params string[] ids) =>
        (await scenario.AuditRecordsAsync()).Count(record => record.TenantId is { } tenant && ids.Contains(tenant));

    private static async Task AssertGoneAsync(AdminScenario scenario, TemporaryFolder studio, string id)
    {
        Assert.False(await RowExistsAsync(scenario, id));
        Assert.Empty(await scenario.HostsAsync(id));
        Assert.Empty(await scenario.KeysAsync(id));
        Assert.False(await BackupKeyExistsAsync(scenario, id));
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0, await scenario.CountRolesAsync(id));
        Assert.False(Directory.Exists(Path.Combine(studio.Path, id)));
        foreach (var table in await TablesWithTenantIdAsync(scenario))
        {
            Assert.Equal(0L, await CountRowsAsync(scenario, table, id));
        }
    }

    private static async Task<bool> RowExistsAsync(AdminScenario scenario, string id)
    {
        await using var context = NewContext(scenario);
        return await context.Tenants.AnyAsync(row => row.Id == id, CancellationToken);
    }

    private static async Task<bool> BackupKeyExistsAsync(AdminScenario scenario, string id)
    {
        await using var context = NewContext(scenario);
        return await context.TenantBackupKeys.AnyAsync(row => row.TenantId == id, CancellationToken);
    }

    private TenantPurge PurgeFor(AdminScenario scenario, TemporaryFolder studio) =>
        new(
            new NpgsqlConnectionStringBuilder(scenario.AdminConnection),
            scenario.Platform,
            AdminScenario.Cipher,
            new HttpClient(_handler, disposeHandler: false),
            new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret),
            studio.Path);

    private Dictionary<string, string?> Settings(TemporaryFolder studio) => new()
    {
        ["Tenancy:Source"] = "directory",
        ["Cell:Id"] = "vn-1",
        ["Cell:Region"] = "vn",
        ["Cell:TenantDomain"] = "vn.example.test",
        ["Cell:BackupKeyFile"] = _cellKeyFile,
        ["Studio:ApplicationsPath"] = studio.Path,
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    private static PlatformDbContext NewContext(AdminScenario scenario) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);

    private async Task<bool> RealmExistsAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}");
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.OK;
    }

    // The redirect addresses of the platform client "axis" in the tenant's realm.
    private async Task<string[]> RedirectAddressesAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}/clients?clientId=axis");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clients = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var client = clients.EnumerateArray().Single(candidate => candidate.GetProperty("clientId").GetString() == "axis");
        return [.. client.GetProperty("redirectUris").EnumerateArray().Select(address => address.GetString()!)];
    }

    private async Task<string> TokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, CancellationToken);
    }
}
