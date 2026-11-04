using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The publisher runs the <c>create</c> job in a cell, in a real Keycloak and a real Mailpit: <c>tenant</c>, <c>application</c>, <c>activate</c>,
/// <c>invite</c> and <c>resume</c>. The tenant stays <c>provisioning</c> until the last step. A step that throws is retried at the next claim and
/// reuses the work that is done. A publisher without a setting that a create step needs still runs, and only the create job fails, at the first
/// step that needs it, with a message that names the setting.
/// </summary>
public sealed class ProvisioningCreateJobTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Template = "request-approval";

    private const string ApplicationFolder = "request-approval";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private readonly List<string> _folders = [];
    private Uri _baseAddress = null!;
    private string _cellKeyFile = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _cellKeyFile = Path.Combine(Path.GetTempPath(), $"axis-cell-key-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.key");
        await File.WriteAllTextAsync(_cellKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n", CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        foreach (var folder in _folders)
        {
            Directory.Delete(folder, recursive: true);
        }

        File.Delete(_cellKeyFile);
        _handler.Dispose();
    }

    [Fact]
    public async Task A_create_job_creates_the_tenant_copies_the_template_activates_it_invites_the_administrator_and_ends_active()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var email = NewEmail();
        var applications = NewApplicationsFolder();
        var settings = Settings(applications);
        var job = await QueueAsync(scenario, settings, id, email);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: settings, identityProvider: _handler);
        try
        {
            await WaitUntilAsync(async () => await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'") is "succeeded" or "failed", "The job did not end.", TimeSpan.FromSeconds(180));
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await run;

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(["tenant", "application", "activate", "invite", "resume"], await StepNamesAsync(scenario, job));
        Assert.Equal(5L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.jobs j, jsonb_array_elements(j.steps) s WHERE j.id = '{job}' AND s ->> 'state' = 'succeeded'"));

        var row = await scenario.RowAsync(id);
        Assert.Equal("active", row.Status);
        Assert.Equal([$"{id}.vn.example.test"], await scenario.HostsAsync(id));
        Assert.Contains(id, await RealmNamesAsync());
        Assert.True(File.Exists(Path.Combine(applications, id, ApplicationFolder, "application.json")));
        Assert.DoesNotContain(Directory.GetDirectories(Path.Combine(applications, id)), folder => Path.GetFileName(folder).StartsWith('.'));

        await using var connection = await TenantConnectionAsync(scenario, id);
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM axis.active_releases"));
        var userId = (await GetJsonAsync($"admin/realms/{id}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray().Single().GetProperty("id").GetString()!;
        Assert.Equal([new GivenRole(null, UserInvitations.AdministratorKind)], await GivenRoles.ListAsync(connection, userId, CancellationToken));
        Assert.Single(await InvitationFlow.WaitForMailsAsync(mailpit, email, 1));

        // The job's own record is the only one, and it names the operator.
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant create'"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant create' AND actor = 'operator-1'"));
        Assert.Equal(0L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'activate'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_create_job_whose_activate_step_throws_once_ends_succeeded_at_the_next_claim_with_one_realm_and_one_application_folder()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var email = NewEmail();
        var applications = NewApplicationsFolder();
        var settings = Settings(applications);
        using var http = new HttpClient(_handler, disposeHandler: false);
        var activations = 0;
        var steps = CreateJob.Steps(Read(scenario, settings), http, TextWriter.Null, TextWriter.Null)
            .Select(step => step.Name != "activate"
                ? step
                : step with
                {
                    RunAsync = (tenantId, input, ct) => ++activations == 1
                        ? throw new InvalidOperationException("The activation broke.")
                        : step.RunAsync(tenantId, input, ct),
                })
            .ToList();
        var kinds = new Dictionary<string, IReadOnlyList<JobStep>> { ["create"] = steps };
        var job = await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "create", id, "operator-1", null, null, CancellationToken, Input(email));
        var jobs = new ProvisioningJobs(scenario.Platform, kinds, TimeSpan.FromMinutes(5));

        Assert.True(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));
        Assert.Equal("running", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);
        Assert.Equal(0L, await ScalarAsync(scenario, "SELECT count(*) FROM platform.audit_records"));

        Assert.True(await jobs.RunNextAsync(TextWriter.Null, CancellationToken));

        Assert.Equal("succeeded", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal(2, await ScalarAsync(scenario, $"SELECT claims FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("active", (await scenario.RowAsync(id)).Status);
        Assert.Single(await RealmNamesAsync(), name => name == id);
        var folders = Directory.GetDirectories(Path.Combine(applications, id));
        Assert.Equal([ApplicationFolder], folders.Select(Path.GetFileName));
        await using var connection = await TenantConnectionAsync(scenario, id);
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM axis.active_releases"));
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant create'"));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_publisher_without_the_studio_applications_path_fails_the_create_job_at_the_application_step_and_keeps_running()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var settings = Settings(NewApplicationsFolder());
        settings.Remove("Studio:ApplicationsPath");
        var job = await QueueAsync(scenario, settings, id, NewEmail());

        // The tenant step needs Keycloak, so the test marks it done: only the missing setting is under test.
        await ScalarAsync(scenario, $"UPDATE platform.jobs SET steps = jsonb_set(steps, '{{0,state}}', '\"succeeded\"') WHERE id = '{job}'");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: settings, identityProvider: _handler);
        try
        {
            await WaitUntilAsync(async () => await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'") is "succeeded" or "failed", "The job did not end.", TimeSpan.FromSeconds(60));
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await run;

        Assert.Equal(0, result.Exit);
        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT state FROM platform.jobs WHERE id = '{job}'"));
        Assert.Equal("failed", await ScalarAsync(scenario, $"SELECT steps -> 1 ->> 'state' FROM platform.jobs WHERE id = '{job}'"));
        Assert.Contains("Studio:ApplicationsPath", (string)(await ScalarAsync(scenario, $"SELECT error FROM platform.jobs WHERE id = '{job}'"))!, StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync(scenario, $"SELECT count(*) FROM platform.audit_records WHERE tenant_id = '{id}' AND command = 'tenant create' AND actor = 'operator-1'"));
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static string Input(string email) => JsonSerializer.Serialize(new { application = Template, administrator = email });

    private string NewApplicationsFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"axis-create-job-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}");
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }

    // The settings of a publisher in a cell: the directory source, Development, which allows a mail server without TLS, and the real identity provider.
    private Dictionary<string, string?> Settings(string applications) => new()
    {
        ["Tenancy:Source"] = "directory",
        ["DOTNET_ENVIRONMENT"] = "Development",
        ["Cell:Id"] = "vn-1",
        ["Cell:Region"] = "vn",
        ["Cell:TenantDomain"] = "vn.example.test",
        ["Cell:BackupKeyFile"] = _cellKeyFile,
        ["Templates:Path"] = SampleTemplates.Path,
        ["Studio:ApplicationsPath"] = applications,
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
        ["Email:Smtp:Host"] = MailpitFixture.NetworkAlias,
        ["Email:Smtp:Port"] = $"{MailpitFixture.NetworkSmtpPort}",
        ["Email:Smtp:UseTls"] = "false",
        ["Email:From"] = "noreply@example.test",
    };

    // The create steps that the publisher would build from the same settings.
    private static CreateJobSettings Read(AdminScenario scenario, Dictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["Admin:ConnectionString"] = scenario.AdminConnection,
            ["ConnectionStrings:PlatformSchema"] = scenario.Platform,
            ["Platform:SecretKey"] = AdminScenario.TestSecretKey,
            ["Extensions:Path"] = SamplePackages.Path,
            ["Modules:Path"] = SampleModules.Path,
        };
        foreach (var (name, value) in settings)
        {
            values[name] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var activate = AdminCommands.ReadActivateSettings(configuration, TextWriter.Null, out var problem);
        Assert.True(activate is not null, problem);
        return AdminCommands.ReadCreateJobSettings(configuration, CellOptions.Load(configuration), activate);
    }

    private static async Task<Guid> QueueAsync(AdminScenario scenario, Dictionary<string, string?> settings, string id, string email)
    {
        using var http = new HttpClient();
        var kinds = new Dictionary<string, IReadOnlyList<JobStep>> { ["create"] = CreateJob.Steps(Read(scenario, settings), http, TextWriter.Null, TextWriter.Null) };
        return await ProvisioningJobs.QueueAsync(scenario.Platform, kinds, "create", id, "operator-1", null, null, CancellationToken, Input(email));
    }

    private static async Task<string[]> StepNamesAsync(AdminScenario scenario, Guid job) =>
        JsonSerializer.Deserialize<JsonElement>((string)(await ScalarAsync(scenario, $"SELECT steps::text FROM platform.jobs WHERE id = '{job}'"))!)
            .EnumerateArray().Select(step => step.GetProperty("name").GetString()!).ToArray();

    private static async Task<object?> ScalarAsync(AdminScenario scenario, string sql)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(CancellationToken);
        return value is DBNull ? null : value;
    }

    private static async Task<NpgsqlConnection> TenantConnectionAsync(AdminScenario scenario, string id)
    {
        var row = await scenario.RowAsync(id);
        var password = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var connection = new NpgsqlConnection(scenario.TenantConnectionString(id, row.SchemaUser, password));
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<string[]> RealmNamesAsync() =>
        (await GetJsonAsync("admin/realms")).EnumerateArray().Select(realm => realm.GetProperty("realm").GetString()!).ToArray();

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await MasterTokenAsync(http), HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<string> MasterTokenAsync(HttpClient http)
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
