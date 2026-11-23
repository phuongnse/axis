using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Processes.Storage;
using Axis.Studio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Each <c>write_resource</c>, <c>delete_resource</c> and <c>write_layer</c> call of the MCP server stores one finished <c>write</c> step with
/// its outcome and its resource on the person's authoring request that holds the draft, only while that request is <c>working</c> or <c>fixing</c>,
/// see docs/reference/authoring-requests.md#states. A call on any other draft stores none. The server is tenant <c>a</c> on a real PostgreSQL
/// database, and the folder holds the purchase-requests sample.
/// </summary>
public sealed class McpWriteStepTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";
    private const string Module = "department-budgets";
    private const string ProcessId = "94f1cc25-6a68-435f-9383-36fc43aae860";

    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-steps-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-steps-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _token;
    private string? _session;
    private string? _subject;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task Each_write_tool_call_on_a_working_request_stores_one_write_step_with_its_outcome_and_resource()
    {
        var (draft, request) = await SeedRequestAsync("working");
        var role = Guid.NewGuid().ToString("D");

        // A create names its resource in its content, and the saved step holds what the draft wrote.
        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(Role(role, "reviewer")))).IsError);
        Assert.False((await CallAsync("write_layer", Arguments(("draft", draft), ("module", Module), ("id", ProcessId)), Content(await LayerContentAsync()))).IsError);

        // A refused patch holds the kind and the name the draft has, not the kind the call asked for.
        var kindChange = new JsonObject { ["id"] = role, ["kind"] = "rule", ["name"] = "reviewer", ["formatVersion"] = 1 };
        Assert.True((await CallAsync("write_resource", Arguments(("draft", draft)), Content(kindChange.ToJsonString()))).IsError);

        // A delete holds the kind and the name the draft had before the call, and a resource the draft lacks holds only the id.
        Assert.False((await CallAsync("delete_resource", Arguments(("draft", draft), ("id", role)))).IsError);
        var missing = Guid.NewGuid().ToString("D");
        Assert.True((await CallAsync("delete_resource", Arguments(("draft", draft), ("id", missing)))).IsError);

        Assert.Equal(
            string.Join(
                "\n",
                $"write_resource:saved:role:{role}:reviewer",
                $"write_layer:saved:process:{ProcessId}:BudgetApproval",
                $"write_resource:refused:role:{role}:reviewer",
                $"delete_resource:saved:role:{role}:reviewer",
                $"delete_resource:refused:null:{missing}:null"),
            await StepsAsync(request));
        Assert.Equal("5", await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{request}' AND finished_at IS NOT NULL AND kind = 'write'"));
        Assert.Equal("1,2,3,4,5", await ScalarAsync($"SELECT string_agg(ordinal::text, ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{request}'"));
    }

    [Fact]
    public async Task A_fixing_request_stores_write_steps_after_its_earlier_steps()
    {
        var (draft, request) = await SeedRequestAsync("fixing");
        await SetAsync(
            $"INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, outcome) VALUES ('{Guid.NewGuid()}', '{request}', 1, 'check', 'failed')");

        var role = Guid.NewGuid().ToString("D");
        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(Role(role, "reviewer")))).IsError);

        Assert.Equal("1:check,2:write", await ScalarAsync($"SELECT string_agg(ordinal || ':' || kind, ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{request}'"));
    }

    [Theory]
    [InlineData("planning")]
    [InlineData("awaitingApproval")]
    [InlineData("checking")]
    [InlineData("reviewing")]
    [InlineData("ready")]
    [InlineData("accepted")]
    [InlineData("dropped")]
    [InlineData("failed")]
    public async Task A_write_while_the_request_is_in_another_state_stores_no_step(string state)
    {
        var (draft, request) = await SeedRequestAsync(state);

        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(Role(Guid.NewGuid().ToString("D"), "reviewer")))).IsError);

        Assert.Equal("0", await ScalarAsync($"SELECT count(*) FROM axis.authoring_request_steps WHERE request_id = '{request}'"));
    }

    [Fact]
    public async Task A_write_on_the_draft_of_another_author_or_on_a_draft_no_request_holds_stores_no_step()
    {
        var before = await ScalarAsync("SELECT count(*) FROM axis.authoring_request_steps");
        var (draft, request) = await SeedRequestAsync("working");
        await SetAsync($"UPDATE axis.authoring_requests SET author = 'someone-else' WHERE id = '{request}'");

        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(Role(Guid.NewGuid().ToString("D"), "reviewer")))).IsError);

        // A draft that no request holds, and a draft id that is not one.
        var lonely = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        Assert.False((await CallAsync("write_resource", Arguments(("draft", lonely)), Content(Role(Guid.NewGuid().ToString("D"), "approver")))).IsError);
        Assert.True((await CallAsync("write_resource", Arguments(("draft", "not-a-draft")), Content(Role(Guid.NewGuid().ToString("D"), "approver")))).IsError);
        Assert.Equal(before, await ScalarAsync("SELECT count(*) FROM axis.authoring_request_steps"));
    }

    public async ValueTask InitializeAsync()
    {
        var authority = await keycloak.StartAsync();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        var platform = await database.CreateDatabaseAsync("mcp_write_steps", CancellationToken);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
        }

        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(database.ConnectionString).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(database.ConnectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Tenants:a:Oidc:Authority", authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", _applications);
            builder.UseSetting("Studio:RevisionsPath", _revisions);
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{Host}"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        _token = await McpTestCalls.RequestTokenAsync(authority, "author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret, CancellationToken);
        using var initialized = await McpTestCalls.PostAsync(_client, Host, _token, McpTestCalls.Initialize(Version), null, null, null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        _session = Assert.Single(initialized.Headers.GetValues("Mcp-Session-Id"));
        _subject = new JwtSecurityTokenHandler().ReadJwtToken(_token).Subject;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Directory.Delete(_applications, recursive: true);
        Directory.Delete(_revisions, recursive: true);
    }

    // A draft of the person and an authoring request of the person in the given state that holds it.
    private async Task<(string Draft, Guid Request)> SeedRequestAsync(string state)
    {
        var draft = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        var request = Guid.NewGuid();
        await SetAsync(
            $"INSERT INTO axis.authoring_requests (id, application_id, author, text, state, draft_id) VALUES ('{request}', '{StudioFolder.ReadManifest(Folder)!.Id}', '{_subject}', 'Add a field.', '{state}', '{draft}')");
        return (draft, request);
    }

    // The write steps in order as tool:outcome:kind:id:name, a part the step lacks as null.
    private Task<string> StepsAsync(Guid request) =>
        ScalarAsync(
            $"SELECT string_agg(input_summary || ':' || outcome || ':' || coalesce(resource->>'kind', 'null') || ':' || coalesce(resource->>'id', 'null') || ':' || coalesce(resource->>'name', 'null'), E'\\n' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{request}' AND kind = 'write'");

    // The module process with one step added, as the layer tool takes it.
    private async Task<string> LayerContentAsync()
    {
        var edited = (await CallAsync("read_module_resource", Arguments(("module", Module), ("id", ProcessId)))).Output!["content"]!.DeepClone();
        ((JsonArray)edited["steps"]!).Add(JsonNode.Parse(
            """
            {
              "name": "financeReview",
              "type": "task",
              "assignee": { "role": "approver" },
              "form": "BudgetReview",
              "outcomes": [
                { "name": "approve", "next": "markApproved" },
                { "name": "reject", "next": "markRejected" }
              ]
            }
            """));
        return edited.ToJsonString();
    }

    private async Task SetAsync(string sql) => await ScalarAsync(sql);

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static (string Name, string Value) Content(string json) => ("content", json);

    private static string Role(string id, string name) =>
        new JsonObject { ["id"] = id, ["kind"] = "role", ["name"] = name, ["formatVersion"] = 1, ["label"] = new JsonObject { ["textKey"] = "role.employee" } }.ToJsonString();

    private static string Arguments(params (string Name, string Value)[] more) =>
        new JsonObject([
            new("application", Application),
            .. more.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Name, pair.Value)),
        ]).ToJsonString();

    // A tool call whose arguments are the application and the properties given. An argument given as JSON, the content, is sent as that JSON.
    private async Task<ToolAnswer> CallAsync(string tool, string arguments, (string Name, string Value)? json = null)
    {
        if (json is { } given)
        {
            var node = JsonNode.Parse(arguments)!.AsObject();
            node[given.Name] = JsonNode.Parse(given.Value);
            arguments = node.ToJsonString();
        }

        using var response = await McpTestCalls.PostAsync(
            _client!, Host, _token, McpTestCalls.ToolCall(tool, arguments), _session, Version, null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!["result"]!;
        return result["isError"]!.GetValue<bool>()
            ? new ToolAnswer(true, null, result["content"]![0]!["text"]!.GetValue<string>())
            : new ToolAnswer(false, result["structuredContent"], null);
    }

    private sealed record ToolAnswer(bool IsError, JsonNode? Output, string? Error);
}
