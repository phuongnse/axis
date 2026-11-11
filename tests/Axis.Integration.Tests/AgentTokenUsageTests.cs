using System.Net;
using System.Text;
using Axis.Agent;
using Axis.Agent.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The agent host adds the tokens of every model call of a tenant to the tenant's usage of the UTC day, by binding and model, and counts the tokens
/// of the tenant's own key apart. A usage write that fails never fails the call, see <c>docs/reference/tenant-quotas.md#usage</c>.
/// </summary>
public sealed class AgentTokenUsageTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string TenantKey = "sk-tenant-a-6601234567890";

    private const string Answer = """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":4}}""";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-agent-token-usage-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task The_tokens_of_each_tenants_calls_are_added_to_its_usage_of_the_day_by_binding_and_model()
    {
        var platform = await CellTestPlatform.CreateAsync(database, new Uri("https://127.0.0.1:1/realms/axis"), CancellationToken);
        await using var app = Build(platform, allowTenantKeys: "false", new LogCollector());
        var adapters = app.Services.GetRequiredService<TenantModelAdapters>();

        await Call(adapters, "a");
        await Call(adapters, "a");
        await Call(adapters, "b");

        Assert.Equal(
            ["a|main|model-1|False|6|8", "b|main|model-1|False|3|4"],
            await RowsAsync(platform));
        Assert.Equal(2L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_token_usage WHERE day = (now() AT TIME ZONE 'UTC')::date", CancellationToken));
    }

    [Fact]
    public async Task The_tokens_of_a_tenants_own_key_are_counted_apart_from_the_tokens_of_the_installations_key()
    {
        var platform = await CellTestPlatform.CreateAsync(database, new Uri("https://127.0.0.1:1/realms/axis"), CancellationToken);
        await AgentTenantModelKeyTests.StoreKeyAsync(platform, "a", TenantKey);
        await using var app = Build(platform, allowTenantKeys: "true", new LogCollector());
        var adapters = app.Services.GetRequiredService<TenantModelAdapters>();

        await Call(adapters, "a");
        await PostgreSqlServer.AdminScalarAsync(platform, "UPDATE platform.tenants SET model_key = NULL WHERE id = 'a'", CancellationToken);
        await Call(adapters, "a");

        Assert.Equal(
            ["a|main|model-1|False|3|4", "a|main|model-1|True|3|4"],
            await RowsAsync(platform));
    }

    [Fact]
    public async Task A_usage_write_that_fails_logs_a_warning_that_names_the_tenant_and_the_call_still_answers()
    {
        var platform = await CellTestPlatform.CreateAsync(database, new Uri("https://127.0.0.1:1/realms/axis"), CancellationToken);
        var readerRole = new NpgsqlConnectionStringBuilder(PostgreSqlServer.RuntimeConnectionString(platform)).Username;
        await PostgreSqlServer.AdminScalarAsync(platform, $"REVOKE INSERT, UPDATE ON platform.tenant_token_usage FROM \"{readerRole}\"", CancellationToken);
        var logs = new LogCollector();
        await using var app = Build(platform, allowTenantKeys: "false", logs);

        var answer = await Call(app.Services.GetRequiredService<TenantModelAdapters>(), "a");

        Assert.Equal(new ModelTokens(3, 4), answer.Tokens);
        Assert.Contains(logs.Entries, entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("tenant a ", StringComparison.Ordinal));
        Assert.Empty(await RowsAsync(platform));
    }

    private static async Task<ModelAnswer> Call(TenantModelAdapters adapters, string tenantId)
    {
        var adapter = await adapters.ForAsync(tenantId, "main", CancellationToken);
        return await adapter.CallAsync(new ModelCall("You build applications.", [new ModelMessage(ModelRole.User, [new ModelText("Add a field.")])], []), CancellationToken);
    }

    // Each row as tenant|binding|model|own key|input tokens|output tokens, in key order.
    private static async Task<List<string>> RowsAsync(string platform)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id, binding, model, own_key, input_tokens, output_tokens FROM platform.tenant_token_usage ORDER BY tenant_id, own_key",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetBoolean(3)}|{reader.GetInt64(4)}|{reader.GetInt64(5)}");
        }

        return rows;
    }

    private WebApplication Build(string platform, string allowTenantKeys, LogCollector logs)
    {
        var keyFile = Path.Combine(_folder, "key");
        File.WriteAllText(keyFile, "sk-installation-9901234567890\n");
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
            services => services.AddHttpClient(ModelAdapters.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new AnswerHandler()));
    }

    // Answers like the Anthropic Messages API, with 3 input tokens and 4 output tokens.
    private sealed class AnswerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") });
    }
}
