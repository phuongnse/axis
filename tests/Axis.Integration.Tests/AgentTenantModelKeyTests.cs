using System.Net;
using System.Text;
using Axis.Agent;
using Axis.Agent.Models;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// With <c>Ai:AllowTenantKeys</c> on, the agent host sends a tenant's own model key on that tenant's calls and the installation's key on every other
/// tenant's calls. With it off, it sends the installation's key, see <c>docs/reference/model-bindings.md#a-tenants-own-key</c>.
/// </summary>
public sealed class AgentTenantModelKeyTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string InstallationKey = "sk-installation-8801234567890";
    private const string TenantKey = "sk-tenant-a-7701234567890";

    private const string Answer = """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":4}}""";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-agent-model-key-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task With_the_setting_on_a_tenants_key_is_sent_on_its_calls_only_and_a_removed_key_is_not_sent_from_the_next_call()
    {
        var platform = await CellTestPlatform.CreateAsync(database, new Uri("https://127.0.0.1:1/realms/axis"), CancellationToken);
        await StoreKeyAsync(platform, "a", TenantKey);
        var logs = new LogCollector();
        var handler = new KeyRecordingHandler();
        await using var app = Build(platform, allowTenantKeys: "true", logs, handler);
        var adapters = app.Services.GetRequiredService<TenantModelAdapters>();

        await Call(adapters, "a");
        await Call(adapters, "b");
        await PostgreSqlServer.AdminScalarAsync(platform, "UPDATE platform.tenants SET model_key = NULL WHERE id = 'a'", CancellationToken);
        await Call(adapters, "a");

        Assert.Equal([TenantKey, InstallationKey, InstallationKey], handler.Keys);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(TenantKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_setting_off_a_stored_tenant_key_is_not_sent()
    {
        var platform = await CellTestPlatform.CreateAsync(database, new Uri("https://127.0.0.1:1/realms/axis"), CancellationToken);
        await StoreKeyAsync(platform, "a", TenantKey);
        var logs = new LogCollector();
        var handler = new KeyRecordingHandler();
        await using var app = Build(platform, allowTenantKeys: "false", logs, handler);

        await Call(app.Services.GetRequiredService<TenantModelAdapters>(), "a");

        Assert.Equal([InstallationKey], handler.Keys);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(TenantKey, StringComparison.Ordinal));
    }

    private static async Task Call(TenantModelAdapters adapters, string tenantId)
    {
        var adapter = await adapters.ForAsync(tenantId, "main", CancellationToken);
        await adapter.CallAsync(new ModelCall("You build applications.", [new ModelMessage(ModelRole.User, [new ModelText("Add a field.")])], []), CancellationToken);
    }

    private WebApplication Build(string platform, string allowTenantKeys, LogCollector logs, HttpMessageHandler handler)
    {
        var keyFile = Path.Combine(_folder, "key");
        File.WriteAllText(keyFile, InstallationKey + "\n");
        return AgentTestHost.Build(
            aiOn: true,
            new Dictionary<string, string?>
            {
                ["Tenancy:Source"] = "directory",
                ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
                ["Platform:SecretKey"] = TestPlatform.SecretKey,
                ["Ai:Bindings:main:Kind"] = "anthropic",
                ["Ai:Bindings:main:Endpoint"] = "http://models.example",
                ["Ai:Bindings:main:Model"] = "model-1",
                ["Ai:Bindings:main:KeyFile"] = keyFile,
                ["Ai:AllowTenantKeys"] = allowTenantKeys,
            },
            logs,
            services => services.AddHttpClient(ModelAdapters.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler));
    }

    internal static async Task StoreKeyAsync(string platform, string tenantId, string key)
    {
        var cipher = TenantSecretCipher.Load(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestPlatform.SecretKey }).Build());
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("UPDATE platform.tenants SET model_key = @key WHERE id = @id", connection);
        command.Parameters.AddWithValue("key", cipher.Encrypt(tenantId, TenantSecretFields.ModelKey, key));
        command.Parameters.AddWithValue("id", tenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    // Records the key of each call and answers like the Anthropic Messages API.
    private sealed class KeyRecordingHandler : HttpMessageHandler
    {
        public List<string?> Keys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Keys.Add(request.Headers.TryGetValues("x-api-key", out var values) ? string.Join(",", values) : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") });
        }
    }
}
