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
/// <c>run_scenarios</c> of the MCP server stores a <c>test</c> publish request from the files of the person's draft, and the
/// publisher runs it as any <c>test</c>, see docs/reference/mcp-server.md#tools. A call with the request then answers the results.
/// The server is tenant <c>a</c> on a real PostgreSQL database, and the folder holds the purchase-requests sample.
/// </summary>
[Collection("Test scenario runs")]
public sealed class McpRunScenariosTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";
    private const string Passing = "ApprovalBelowThreshold";
    private const string FailingFile = "scenarios/failing.json";

    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-run-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-run-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _token;
    private string? _session;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task The_publisher_runs_the_draft_of_a_run_and_a_call_with_the_request_answers_a_passed_and_a_failed_scenario_while_the_working_folder_does_not_change()
    {
        var before = FolderState();
        var draft = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        var failing = FailingScenario();
        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), ("content", failing))).IsError);

        var started = await CallAsync("run_scenarios", Arguments(("draft", draft)), ("names", $"""["{Passing}","Failing"]"""));

        Assert.False(started.IsError, started.Error);
        Assert.Equal(["diagnostics", "request", "scenarios", "status"], started.Output!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("queued", started.Output["status"]!.GetValue<string>());
        Assert.Empty(started.Output["diagnostics"]!.AsArray());
        Assert.Empty(started.Output["scenarios"]!.AsArray());
        var request = started.Output["request"]!.GetValue<string>();

        await using var publisher = StudioPublishTests.RunPublisher("a", Host, database.ConnectionString);
        var finished = await WaitForFinalAsync(draft, request);

        Assert.Equal(request, finished["request"]!.GetValue<string>());
        Assert.Equal("failed", finished["status"]!.GetValue<string>());
        var results = finished["scenarios"]!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.Equal("passed", Assert.Single(results, item => item!["name"]!.GetValue<string>() == Passing)!["outcome"]!.GetValue<string>());
        var failed = Assert.Single(results, item => item!["name"]!.GetValue<string>() == "Failing")!;
        Assert.Equal("failed", failed["outcome"]!.GetValue<string>());
        Assert.NotNull(failed["step"]);

        // The stored request is a test of the draft, and its snapshot holds the draft's file, not the working folder's.
        Assert.Equal(
            $"test|tenant|{draft}",
            await ScalarAsync($"SELECT kind || '|' || target || '|' || draft_id FROM axis.publish_requests WHERE id = '{request}'"));
        Assert.Equal(
            1L,
            await ScalarAsync($"SELECT count(*) FROM axis.publish_requests r, jsonb_array_elements(r.snapshot) f WHERE r.id = '{request}' AND f->>'file' = '{FailingFile}'"));
        Assert.Equal(before, FolderState());
        Assert.False(File.Exists(Path.Combine(Folder, FailingFile)));
    }

    public async ValueTask InitializeAsync()
    {
        var authority = await keycloak.StartAsync();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        var platform = await database.CreateDatabaseAsync("mcp_run_scenarios", CancellationToken);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
        }

        // The publisher serves a tenant only when its database holds every migration, the processes module's too.
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

    // A copy of the sample's passing scenario with a new id and name, whose last step expects the wrong status.
    private string FailingScenario()
    {
        var original = File.ReadAllText(Path.Combine(Folder, "scenarios", "approval-below-threshold.json"));
        var content = original
            .Replace("09d75317-fd9e-411e-8322-317778fc1b2b", "7a1c2f0e-5d3b-4b8e-9c61-0f2d4e8a6b13", StringComparison.Ordinal)
            .Replace($"\"{Passing}\"", "\"Failing\"", StringComparison.Ordinal)
            .Replace("\"status\": \"approved\"", "\"status\": \"rejected\"", StringComparison.Ordinal);
        Assert.NotEqual(original, content);
        return content;
    }

    // Polls the call with the request until the publisher has finished it.
    private async Task<JsonNode> WaitForFinalAsync(string draft, string request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            var answer = await CallAsync("run_scenarios", Arguments(("draft", draft), ("request", request)));
            Assert.False(answer.IsError, answer.Error);
            if (answer.Output!["status"]!.GetValue<string>() is "succeeded" or "failed")
            {
                return answer.Output;
            }

            await Task.Delay(200, CancellationToken);
        }

        Assert.Fail("The request did not end in time. Is the publisher running?");
        return null!;
    }

    private static string Arguments(params (string Name, string Value)[] more) =>
        new JsonObject([
            new("application", Application),
            .. more.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Name, pair.Value)),
        ]).ToJsonString();

    // The id and version of every resource of the working folder.
    private List<string> FolderState() => [.. StudioFolder.List(Folder).Select(file => $"{file.Id:D} {file.Version}")];

    private Task<object?> ScalarAsync(string sql) => PostgreSqlServer.AdminScalarAsync(database.ConnectionString, sql, CancellationToken);

    // A tool call whose arguments are the application and the properties given. An argument given as JSON, the content and the names, is sent as that JSON.
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
