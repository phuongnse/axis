using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The publisher in a cell queues and runs a <c>purge</c> job for each main tenant whose <c>delete_after</c> has passed, on real PostgreSQL and Keycloak.
/// The job removes the tenant, its preview tenant and their databases, realm and Studio folders, leaves a tombstone that names the actor of the
/// <c>delete</c> job and ends with one <c>tenant purge</c> audit record. A tenant whose deletion was cancelled, or whose window is still ahead, gets no
/// purge job and keeps its data. A tenant has one purge job at a time, and a preview tenant gets none of its own. A purge that fails, here for a missing
/// setting, is queued again only an hour after it ended.
/// </summary>
public sealed class ProvisioningPurgeJobTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static readonly string[] _stepNames = ["hosts", "database", "realm", "files", "studio", "keys", "rows", "directory"];

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private readonly string _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        await File.WriteAllTextAsync(_cellKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n", CancellationToken);
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
    public async Task The_publisher_purges_a_tenant_whose_window_has_passed_with_the_actor_of_the_delete_job_and_gives_its_preview_tenant_no_job()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var id = await CreateTenantAsync(scenario, studio);
        var preview = scenario.NewTenantId();
        var created = await scenario.RunAsync(
            AdminScenario.PreviewArgs(preview, id, $"{preview}.example.test"), identityProvider: _handler, extra: Settings(studio));
        Assert.Equal(0, created.Exit);
        await WriteStudioFileAsync(studio, preview);
        await DeleteAsync(scenario, id);
        await EndWindowAsync(scenario, id);
        Assert.True(await scenario.DatabaseExistsAsync(id));
        Assert.True(await scenario.DatabaseExistsAsync(preview));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: Settings(studio), identityProvider: _handler);
        try
        {
            await WaitUntilAsync(
                async () => await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge' AND state IN ('succeeded', 'failed')") is 1L,
                "The purge job did not end.",
                TimeSpan.FromSeconds(180));

            // More rounds follow before the publisher stops. The tenant is gone, so none of them queues a purge.
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await run;

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
        Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{preview}' AND kind = 'purge'"));
        Assert.Equal(
            (long)_stepNames.Length,
            await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs j, jsonb_array_elements(j.steps) s WHERE j.tenant_id = '{id}' AND j.kind = 'purge' AND s ->> 'state' = 'succeeded'"));
        Assert.Equal(_stepNames, await StepNamesAsync(scenario, id));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant purge'"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant purge' AND actor = 'operator-1'"));

        foreach (var tenant in new[] { id, preview })
        {
            Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.tenants WHERE id = '{tenant}'"));
            Assert.False(await scenario.DatabaseExistsAsync(tenant));
            Assert.Equal(0, await scenario.CountRolesAsync(tenant));
            Assert.False(Directory.Exists(Path.Combine(studio.Path, tenant)));
            Assert.Equal("operator-1", await ScalarAsync(scenario, $"SELECT actor FROM platform.deleted_tenants WHERE id = '{tenant}'"));
        }

        Assert.False(await RealmExistsAsync(id));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_tenant_whose_deletion_was_cancelled_or_whose_window_is_ahead_gets_no_purge_job_and_keeps_its_data_while_a_due_tenant_has_exactly_one()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var cancelled = await CreateTenantAsync(scenario, studio);
        var ahead = await CreateTenantAsync(scenario, studio);
        var due = await CreateTenantAsync(scenario, studio);
        await DeleteAsync(scenario, cancelled);
        await CancelDeletionAsync(scenario, cancelled);
        await DeleteAsync(scenario, ahead);
        await DeleteAsync(scenario, due);
        await EndWindowAsync(scenario, due);
        Assert.Null(await ScalarAsync(scenario, $"SELECT delete_after FROM platform.tenants WHERE id = '{cancelled}'"));

        using var http = new HttpClient(_handler, disposeHandler: false);
        var jobs = new ProvisioningJobs(scenario.Platform, Kinds(scenario, studio, http), TimeSpan.FromMinutes(5));
        while (await jobs.RunNextAsync(TextWriter.Null, CancellationToken))
        {
        }

        // More rounds do not queue the purge again.
        Assert.False(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));

        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{due}' AND kind = 'purge'"));
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE tenant_id = '{due}' AND kind = 'purge'"));
        Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.tenants WHERE id = '{due}'"));
        Assert.False(await scenario.DatabaseExistsAsync(due));
        Assert.False(await RealmExistsAsync(due));
        foreach (var kept in new[] { cancelled, ahead })
        {
            Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{kept}' AND kind = 'purge'"));
            Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.tenants WHERE id = '{kept}'"));
            Assert.True(await scenario.DatabaseExistsAsync(kept));
            Assert.Equal(2, await scenario.CountRolesAsync(kept));
            Assert.True(await RealmExistsAsync(kept));
            Assert.True(Directory.Exists(Path.Combine(studio.Path, kept)));
            Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.deleted_tenants WHERE id = '{kept}'"));
        }
    }

    [Fact]
    public async Task A_purge_that_fails_for_a_missing_setting_is_queued_again_only_an_hour_after_it_ended()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        var id = await CreateTenantAsync(scenario, studio);
        await DeleteAsync(scenario, id);
        await EndWindowAsync(scenario, id);
        using var http = new HttpClient(_handler, disposeHandler: false);
        const string problem = "The setting Studio:ApplicationsPath is required.";
        var kinds = new Dictionary<string, IReadOnlyList<JobStep>>(ProvisioningJobs.Builtin(scenario.Platform))
        {
            [PurgeJob.Kind] = PurgeJob.Steps(CreateSettings(scenario), JobSetting<string>.Missing(problem), scenario.Platform, AdminScenario.Cipher, http),
        };
        var jobs = new ProvisioningJobs(scenario.Platform, kinds, TimeSpan.FromMinutes(5));
        while (await jobs.RunNextAsync(TextWriter.Null, CancellationToken))
        {
        }

        for (var round = 0; round < 3; round++)
        {
            Assert.False(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
        }

        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
        Assert.Equal(problem, await ScalarAsync(scenario, $"SELECT error FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
        Assert.Equal(_stepNames, await StepNamesAsync(scenario, id));
        Assert.Equal(
            "failed",
            await ScalarAsync(scenario, $"SELECT s ->> 'state' FROM platform.jobs j, jsonb_array_elements(j.steps) s WHERE j.tenant_id = '{id}' AND j.kind = 'purge' AND s ->> 'name' = 'hosts'"));
        Assert.Equal(
            7L,
            await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs j, jsonb_array_elements(j.steps) s WHERE j.tenant_id = '{id}' AND j.kind = 'purge' AND s ->> 'state' = 'pending'"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant purge'"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.tenants WHERE id = '{id}'"));
        Assert.True(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.deleted_tenants WHERE id = '{id}'"));

        // The failure is now more than an hour old, so the next round queues a second purge. It fails the same way.
        await ScalarAsync(scenario, $"UPDATE platform.jobs SET finished_at = now() - interval '2 hours' WHERE tenant_id = '{id}' AND kind = 'purge'");
        Assert.True(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));

        Assert.Equal(2L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
        Assert.Equal(2L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge' AND state = 'failed'"));
        Assert.False(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
        Assert.Equal(2L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"));
    }

    [Fact]
    public async Task The_refusing_steps_of_a_purge_without_a_setting_carry_the_names_of_the_real_steps()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var studio = new TemporaryFolder();
        using var http = new HttpClient(_handler, disposeHandler: false);

        var real = PurgeJob.Steps(CreateSettings(scenario), JobSetting<string>.Of(studio.Path), scenario.Platform, AdminScenario.Cipher, http);
        var refusing = PurgeJob.Steps(JobSetting<CreateSettings>.Missing("The setting IdentityProvider:Url is required."), JobSetting<string>.Of(studio.Path), scenario.Platform, AdminScenario.Cipher, http);

        Assert.Equal(_stepNames, real.Select(step => step.Name));
        Assert.Equal(_stepNames, refusing.Select(step => step.Name));
        Assert.Equal("The setting IdentityProvider:Url is required.", await refusing[0].RunAsync(Guid.NewGuid(), "t", null, CancellationToken));
    }

    private Dictionary<string, IReadOnlyList<JobStep>> Kinds(AdminScenario scenario, TemporaryFolder studio, HttpClient http) =>
        new Dictionary<string, IReadOnlyList<JobStep>>(ProvisioningJobs.Builtin(scenario.Platform))
        {
            [PurgeJob.Kind] = PurgeJob.Steps(CreateSettings(scenario), JobSetting<string>.Of(studio.Path), scenario.Platform, AdminScenario.Cipher, http),
        };

    private JobSetting<CreateSettings> CreateSettings(AdminScenario scenario) =>
        JobSetting<CreateSettings>.Of(new CreateSettings(
            new NpgsqlConnectionStringBuilder(scenario.AdminConnection),
            scenario.Platform,
            AdminScenario.Cipher,
            null,
            new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret)));

    // Queues and runs the delete job as the provisioning API would, with operator-1 as its actor. The runner has no purge kind, so it queues no purge.
    private static async Task DeleteAsync(AdminScenario scenario, string id) => await RunBuiltinAsync(scenario, "delete", id);

    private static async Task CancelDeletionAsync(AdminScenario scenario, string id) => await RunBuiltinAsync(scenario, "cancel-deletion", id);

    private static async Task RunBuiltinAsync(AdminScenario scenario, string kind, string id)
    {
        var builtin = ProvisioningJobs.Builtin(scenario.Platform);
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, builtin, kind, id, "operator-1", null, null, CancellationToken);
        Assert.True(await new ProvisioningJobs(scenario.Platform, builtin, TimeSpan.FromMinutes(5)).RunNextAsync(TextWriter.Null, CancellationToken));
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
    }

    private static async Task EndWindowAsync(AdminScenario scenario, string id) =>
        await ScalarAsync(scenario, $"UPDATE platform.tenants SET delete_after = now() - interval '1 minute' WHERE id = '{id}' OR preview_of = '{id}'");

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

    private static async Task WriteStudioFileAsync(TemporaryFolder studio, string id)
    {
        var file = Path.Combine(studio.Path, id, "app-a", "application.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{\"name\":\"a\"}", CancellationToken);
    }

    private static async Task<string[]> StepNamesAsync(AdminScenario scenario, string id) =>
        JsonSerializer.Deserialize<JsonElement>((string)(await ScalarAsync(scenario, $"SELECT steps::text FROM platform.jobs WHERE tenant_id = '{id}' AND kind = 'purge'"))!)
            .EnumerateArray().Select(step => step.GetProperty("name").GetString()!).ToArray();

    private static async Task<object?> ScalarAsync(AdminScenario scenario, string sql)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(CancellationToken);
        return value is DBNull ? null : value;
    }

    // The settings of a publisher in a cell, with the real identity provider.
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

    private async Task<bool> RealmExistsAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}");
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

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, CancellationToken);
    }
}
