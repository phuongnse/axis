using System.Net;
using System.Text.Json;
using Axis.Agent.Models;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// The messages that a planning step sends to the adapter hold only what <c>docs/reference/authoring-requests.md#what-reaches-the-model</c> lists. The
/// tenant holds a Supplier record, a <c>secret</c> and a <c>url</c> binding, and the author's first and last name, each with a marker of 13 digits. The
/// scripted model calls every read tool and reads every resource that <c>list_resources</c> lists, so the test fails when anything of the four reaches
/// the adapter in the system text, a message, a tool call, a tool result or a tool definition. The model is the scripted adapter, so no test calls a paid model.
/// </summary>
public sealed class AgentPlanningModelInputTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string BudgetApprovalId = "94f1cc25-6a68-435f-9383-36fc43aae860";
    private const string Application = """{"application":"PurchaseRequests"}""";

    private readonly string _firstName = $"Pat{Marker()}";
    private readonly string _lastName = $"Example{Marker()}";
    private readonly string _secret = $"secret-{Marker()}";
    private readonly string _url = $"https://binding-{Marker()}.example.test/api";
    private readonly string _recordCode = $"S{Marker()}";
    private AuthoringRealm _realm = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    [Fact]
    public async Task A_planning_step_that_reads_every_resource_sends_the_adapter_no_record_value_secret_binding_value_or_name_of_the_author()
    {
        using var client = await _realm.SignInAsync();

        // The seeds are live: the author has the names, and the database holds the record.
        using var me = await client.GetAsync("/api/me", CancellationToken);
        var displayName = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("displayName").GetString()!;
        Assert.Contains(_firstName, displayName, StringComparison.Ordinal);
        Assert.Contains(_lastName, displayName, StringComparison.Ordinal);
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("2deb5acd-dfd8-4c5a-b546-a1aa03df6964")));
        await using (var connection = new NpgsqlConnection(_realm.SchemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await using var insert = new NpgsqlCommand($"INSERT INTO {table} (id, f_code, f_name) VALUES (gen_random_uuid(), @code, @name)", connection);
            insert.Parameters.AddWithValue("code", _recordCode);
            insert.Parameters.AddWithValue("name", $"Supplier {_recordCode}");
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        Assert.Equal(1L, await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, $"SELECT count(*) FROM {table} WHERE f_code = '{_recordCode}'", CancellationToken));

        var id = await AgentPlanningTests.SendAsync(client);
        var ids = ResourceIds();
        var script = new ScriptedModelAdapter(
        [
            new ModelAnswer(
                [
                    new ModelToolCall("reference", "read_reference", AgentPlanningTests.Json("""{"topic":"entity"}""")),
                    new ModelToolCall("list", "list_resources", AgentPlanningTests.Json(Application)),
                    new ModelToolCall("names", "read_names", AgentPlanningTests.Json(Application)),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(1, 1)),
            new ModelAnswer(
                [
                    .. ids.Select(resource => new ModelToolCall($"read-{resource}", "read_resource", AgentPlanningTests.Json($$"""{"application":"PurchaseRequests","id":"{{resource}}"}"""))),
                    new ModelToolCall(
                        "module",
                        "read_module_resource",
                        AgentPlanningTests.Json($$"""{"application":"PurchaseRequests","module":"department-budgets","id":"{{BudgetApprovalId}}"}""")),
                ],
                ModelStopReason.ToolUse,
                new ModelTokens(1, 1)),
            AgentPlanningTests.Plan("A plan."),
        ]);
        await using var host = AgentPlanningTests.BuildAgent(_realm, script, new AgentPlanningTests.RecordingHandler(_realm.Factory.Server.CreateHandler()));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        // The scripted model read every resource that the server lists, and no tool answered an error.
        Assert.Equal(3, script.Calls.Count);
        var results = script.Calls[^1].Messages.SelectMany(message => message.Content).OfType<ModelToolResult>().ToList();
        Assert.DoesNotContain(results, result => result.IsError);
        var listed = results.Single(result => result.CallId == "list");
        using var list = JsonDocument.Parse(listed.Content);
        var items = list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();
        Assert.NotEmpty(items);
        Assert.Equal(items.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));

        var sent = script.Calls.SelectMany(Sent).ToList();
        string[] markers = [_firstName, _lastName, _secret, _url, _recordCode];
        var failures = markers
            .SelectMany(marker => sent.Where(text => text.Contains(marker, StringComparison.Ordinal)).Select(text => $"A call to the adapter holds {marker}: {Excerpt(text, marker)}"))
            .ToList();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    public async ValueTask InitializeAsync()
    {
        _realm = new AuthoringRealm(keycloak, database, _firstName, _lastName, Configure);
        await _realm.InitializeAsync();
    }

    public async ValueTask DisposeAsync() => await _realm.DisposeAsync();

    // The server activates the purchase-requests sample at start, which makes its tables, as StudioPublishTests.NewFactory does.
    private void Configure(IWebHostBuilder builder, string folder)
    {
        builder.UseSetting($"Tenants:{AuthoringRealm.Tenant}:Bindings:GuardToken:Kind", "secret");
        builder.UseSetting($"Tenants:{AuthoringRealm.Tenant}:Bindings:GuardToken:Value", _secret);
        builder.UseSetting($"Tenants:{AuthoringRealm.Tenant}:Bindings:GuardUrl:Kind", "url");
        builder.UseSetting($"Tenants:{AuthoringRealm.Tenant}:Bindings:GuardUrl:Value", _url);
        builder.UseSetting($"Tenants:{AuthoringRealm.Tenant}:Bindings:PurchasingSystemUrl:Kind", "url");
        builder.UseSetting($"Tenants:{AuthoringRealm.Tenant}:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
        foreach (var (key, value) in PurchasingBindings.Settings(AuthoringRealm.Tenant).Concat(CompanyBindings.Settings(AuthoringRealm.Tenant)))
        {
            builder.UseSetting(key, value);
        }

        builder.UseSetting("Extensions:Path", SamplePackages.Path);
        builder.UseSetting("Modules:Path", SampleModules.Path);
        builder.UseSetting("ActivateOnStartup:0", folder);
    }

    // A number of 13 digits, which no other text of the test holds, see docs/testing.md#rules-for-tests.
    private static long Marker() => Random.Shared.NextInt64(1_000_000_000_000, 10_000_000_000_000);

    // The id of every resource file of the sample, which is the top-level id of its JSON.
    private static List<string> ResourceIds() =>
        [.. Directory.EnumerateFiles(SamplePath, "*.json", SearchOption.AllDirectories)
            .Select(file => JsonDocument.Parse(File.ReadAllBytes(file)).RootElement)
            .Where(root => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String)
            .Select(root => root.GetProperty("id").GetString()!)];

    // Every text of one call: the system text, the messages and the tool definitions.
    private static IEnumerable<string> Sent(ModelCall call)
    {
        yield return call.System;
        foreach (var part in call.Messages.SelectMany(message => message.Content))
        {
            switch (part)
            {
                case ModelText text:
                    yield return text.Text;
                    break;
                case ModelToolCall toolCall:
                    yield return toolCall.Name + toolCall.Input.GetRawText();
                    break;
                case ModelToolResult result:
                    yield return result.Content;
                    break;
            }
        }

        foreach (var tool in call.Tools)
        {
            yield return tool.Name;
            yield return tool.Description;
            yield return tool.InputSchema.GetRawText();
        }
    }

    private static string Excerpt(string text, string marker)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        return text[Math.Max(0, at - 40)..Math.Min(text.Length, at + marker.Length + 40)];
    }

    private async Task<string> StateAsync(Guid id) =>
        Convert.ToString(
            await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, $"SELECT state FROM axis.authoring_requests WHERE id = '{id}'", CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
