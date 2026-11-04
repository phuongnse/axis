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
/// <see cref="ScratchTenantRemoval"/> on real PostgreSQL and Keycloak. A scratch tenant restored from an archive in a cell is removed with its
/// directory rows, keys, database, roles, realm and Studio folder, and the source tenant stays. A second removal succeeds and changes nothing.
/// An id without the <c>restore-check-</c> prefix is refused and its tenant stays whole.
/// </summary>
[Collection(AdminCellRestoreCheckTests.ScratchTenantCollection)]
public sealed class AdminScratchTenantRemovalTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string ScratchId = "restore-check-20261101";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [ScratchId];
    private readonly string _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");
    private Uri _baseAddress = null!;
    private PostgresClientTools _tools = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _tools = await PostgresClientTools.CreateAsync();
        File.WriteAllText(_cellKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");
    }

    public async ValueTask DisposeAsync()
    {
        // The scratch id is fixed, so the scenario does not clean it up.
        await PostgreSqlServer.DropDatabaseAsync($"axis_{ScratchId}");
        foreach (var role in new[] { "runtime", "schema" })
        {
            await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"""DROP ROLE IF EXISTS "axis_{ScratchId}_{role}" """, CancellationToken);
        }

        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}", null);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
        File.Delete(_cellKeyFile);
    }

    [Fact]
    public async Task A_scratch_tenant_restored_from_an_archive_is_removed_with_its_database_roles_keys_realm_and_Studio_folder()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var source = await RestoreScratchAsync(scenario, studio);

        await RemovalFor(scenario, studio).RemoveAsync(ScratchId, CancellationToken);

        await AssertScratchGoneAsync(scenario, studio);
        Assert.Equal(1, await CountTenantsAsync(scenario, source));
        Assert.True(await scenario.DatabaseExistsAsync(source));
        Assert.Equal(2, await scenario.CountRolesAsync(source));
        Assert.True(Directory.Exists(Path.Combine(studio.Path, source)));
    }

    [Fact]
    public async Task A_second_removal_of_the_same_scratch_tenant_succeeds_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var source = await RestoreScratchAsync(scenario, studio);
        var removal = RemovalFor(scenario, studio);
        await removal.RemoveAsync(ScratchId, CancellationToken);
        var before = await SnapshotAsync(scenario, studio, source);

        await removal.RemoveAsync(ScratchId, CancellationToken);

        Assert.Equal(before, await SnapshotAsync(scenario, studio, source));
        await AssertScratchGoneAsync(scenario, studio);
    }

    [Fact]
    public async Task A_tenant_whose_id_lacks_the_restore_check_prefix_is_refused_and_stays_whole()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(
            AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: Settings(studio));
        Assert.Equal(0, created.Exit);
        var folder = Path.Combine(studio.Path, id, "app-a");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "application.json"), "{}", CancellationToken);
        var keys = (await scenario.KeysAsync(id)).Count;
        Assert.NotEqual(0, keys);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RemovalFor(scenario, studio).RemoveAsync(id, CancellationToken));

        Assert.Equal(1, await CountTenantsAsync(scenario, id));
        Assert.Equal(keys, (await scenario.KeysAsync(id)).Count);
        Assert.Equal([$"{id}.example.test"], await scenario.HostsAsync(id));
        Assert.True(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(2, await scenario.CountRolesAsync(id));
        Assert.True(await RealmExistsAsync(id));
        Assert.True(File.Exists(Path.Combine(folder, "application.json")));

        Assert.True(ScratchTenantRemoval.IsScratchId(ScratchId));
        foreach (var other in new[] { "restore-check", "RESTORE-CHECK-1", "restore-check-../x", "restore-check-a b", id })
        {
            Assert.False(ScratchTenantRemoval.IsScratchId(other), other);
        }
    }

    // Backs up a source tenant of a cell with a Studio file, deletes its realm, and restores the archive into the scratch tenant.
    private async Task<string> RestoreScratchAsync(AdminScenario scenario, TemporaryFolder studio)
    {
        var settings = Settings(studio);
        var source = scenario.NewTenantId();
        _realms.Add(source);
        var created = await scenario.RunAsync(
            AdminScenario.CreateArgs(source, $"{source}.example.test"), identityProvider: _handler, extra: settings);
        Assert.Equal(0, created.Exit);
        var file = Path.Combine(studio.Path, source, "app-a", "application.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{\"name\":\"a\"}", CancellationToken);

        using var archive = new TemporaryFolder();
        var backup = Path.Combine(archive.Path, "backup.tar");
        var backedUp = await scenario.RunAsync(["tenant", "backup", source, "--out", backup], path: _tools.PathSetting, identityProvider: _handler, extra: settings);
        Assert.Equal(0, backedUp.Exit);

        // The realm is gone, so the restore makes a new one named after the scratch tenant.
        await DeleteRealmAsync(source);
        _realms.Remove(source);
        var restored = await scenario.RunAsync(
            ["tenant", "restore", backup, "--tenant", ScratchId, "--host", $"{ScratchId}.internal.test", "--from", source],
            withClientSecret: false,
            path: _tools.PathSetting,
            identityProvider: _handler,
            extra: settings);
        Assert.Equal(0, restored.Exit);
        Assert.Empty(restored.Error);

        Assert.Equal(1, await CountTenantsAsync(scenario, ScratchId));
        await using (var context = NewContext(scenario))
        {
            Assert.True(await context.TenantBackupKeys.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
            Assert.True(await context.TenantDataKeys.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
            Assert.True(await context.TenantHosts.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
        }

        Assert.True(await scenario.DatabaseExistsAsync(ScratchId));
        Assert.True(await scenario.CountRolesAsync(ScratchId) > 0);
        Assert.True(await RealmExistsAsync(ScratchId));
        Assert.True(File.Exists(Path.Combine(studio.Path, ScratchId, "app-a", "application.json")));
        return source;
    }

    private async Task AssertScratchGoneAsync(AdminScenario scenario, TemporaryFolder studio)
    {
        await using (var context = NewContext(scenario))
        {
            Assert.False(await context.Tenants.AnyAsync(row => row.Id == ScratchId, CancellationToken));
            Assert.False(await context.TenantHosts.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
            Assert.False(await context.TenantBindings.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
            Assert.False(await context.TenantDataKeys.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
            Assert.False(await context.TenantBackupKeys.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
            Assert.False(await context.TenantBackups.AnyAsync(row => row.TenantId == ScratchId, CancellationToken));
        }

        Assert.False(await scenario.DatabaseExistsAsync(ScratchId));
        Assert.Equal(0, await scenario.CountRolesAsync(ScratchId));
        Assert.False(await RealmExistsAsync(ScratchId));
        Assert.False(Directory.Exists(Path.Combine(studio.Path, ScratchId)));
    }

    // Everything a removal may touch, to compare before and after a second removal.
    private async Task<string> SnapshotAsync(AdminScenario scenario, TemporaryFolder studio, string source)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, "admin/realms", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var realms = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).EnumerateArray()
            .Select(realm => realm.GetProperty("realm").GetString()!)
            .Order(StringComparer.Ordinal);
        var files = Directory.EnumerateFileSystemEntries(studio.Path, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(studio.Path, path))
            .Order(StringComparer.Ordinal);
        return string.Join(
            '\n',
            await scenario.SnapshotAsync(),
            await scenario.CountEntriesAsync(),
            await scenario.DatabaseExistsAsync(source),
            await scenario.CountRolesAsync(source),
            await scenario.CountRolesAsync(ScratchId),
            string.Join(',', realms),
            string.Join(',', files));
    }

    private ScratchTenantRemoval RemovalFor(AdminScenario scenario, TemporaryFolder studio) =>
        new(
            new NpgsqlConnectionStringBuilder(scenario.AdminConnection),
            scenario.Platform,
            new TenantRealm(new HttpClient(_handler, disposeHandler: false), new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret)),
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

    private static async Task<int> CountTenantsAsync(AdminScenario scenario, string id)
    {
        await using var context = NewContext(scenario);
        return await context.Tenants.CountAsync(row => row.Id == id, CancellationToken);
    }

    private async Task DeleteRealmAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Delete, $"admin/realms/{realm}", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<bool> RealmExistsAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}", null);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.OK;
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

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }
}
