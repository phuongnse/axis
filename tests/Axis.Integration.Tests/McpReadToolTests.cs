using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Studio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// The read tools of the MCP server, <c>list_resources</c>, <c>read_resource</c>, <c>read_module_resource</c> and <c>read_names</c>,
/// see docs/reference/mcp-server.md#tools. The server is tenant <c>a</c> on a real PostgreSQL database, because a draft read holds
/// the folder's write lock, which lives in the platform database. The folder holds the purchase-requests sample.
/// </summary>
public sealed class McpReadToolTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";
    private const string BudgetApprovalId = "94f1cc25-6a68-435f-9383-36fc43aae860";
    private const string BudgetLayerFile = "customizations/department-budgets/BudgetApproval.json";
    private const string NoApplication = "No Studio folder holds an application with this name.";
    private const string NoDraft = "The application has no draft with this id.";
    private const string NoResource = "The application has no resource with this id.";
    private const string McpForeignCursor = "The cursor is not one this output gave.";
    private const string InputMismatch = "The input does not match the tool's input schema.";

    private readonly List<string> _bodies = [];
    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-read-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-read-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _token;
    private string? _session;
    private string? _user;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task The_tools_answer_the_working_folder_without_a_draft_and_the_draft_of_the_person_with_one()
    {
        var files = StudioFolder.List(Folder);
        var process = files.Single(file => file.Kind == "process" && file.Name == "PurchaseRequestApproval");

        var listed = await CallAsync("list_resources", Arguments());
        var items = listed.Output!["items"]!.AsArray();
        Assert.Equal(files.Select(file => file.Id.ToString("D")), items.Select(item => item!["id"]!.GetValue<string>()));
        var item = items.Single(item => item!["id"]!.GetValue<string>() == process.Id.ToString("D"))!;
        Assert.Equal(process.Version, item["version"]!.GetValue<string>());
        Assert.Equal(process.File, item["file"]!.GetValue<string>());
        Assert.NotNull(item["usedBy"]);

        var read = await CallAsync("read_resource", Arguments(("id", process.Id.ToString("D"))));
        Assert.Equal("PurchaseRequestApproval", read.Output!["name"]!.GetValue<string>());
        Assert.Equal(process.Version, read.Output["version"]!.GetValue<string>());
        Assert.Equal(process.Id.ToString("D"), read.Output["content"]!["id"]!.GetValue<string>());

        var names = await CallAsync("read_names", Arguments());
        Assert.Contains(names.Output!["processes"]!.AsArray(), name => name!["name"]!.GetValue<string>() == "PurchaseRequestApproval");

        var drafts = Drafts(_user!);
        var draft = (await drafts.CreateAsync(Revisions(_user!), CancellationToken)).Value!;
        var role = Guid.NewGuid().ToString("D");
        Assert.Null((await drafts.WriteAsync(draft.Id, new JsonObject { ["id"] = role, ["kind"] = "role", ["name"] = "draft-only-role", ["formatVersion"] = 1, ["label"] = new JsonObject { ["textKey"] = "role.employee" } }, CancellationToken)).Refusal);

        var inDraft = await CallAsync("list_resources", Arguments(("draft", draft.Id)));
        Assert.Contains(inDraft.Output!["items"]!.AsArray(), draftItem => draftItem!["id"]!.GetValue<string>() == role);
        Assert.Equal(files.Count + 1, inDraft.Output["items"]!.AsArray().Count);
        var readInDraft = await CallAsync("read_resource", Arguments(("id", role), ("draft", draft.Id)));
        Assert.Equal("draft-only-role", readInDraft.Output!["name"]!.GetValue<string>());
        var namesInDraft = await CallAsync("read_names", Arguments(("draft", draft.Id)));
        Assert.Contains("draft-only-role", namesInDraft.Output!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains((await drafts.FindAsync(draft.Id, CancellationToken)).Value!.Versions.Keys, key => key == Guid.Parse(role));

        var without = await CallAsync("list_resources", Arguments());
        Assert.DoesNotContain(without.Output!["items"]!.AsArray(), withoutItem => withoutItem!["id"]!.GetValue<string>() == role);
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", role)))).Error);
        Assert.DoesNotContain("draft-only-role", (await CallAsync("read_names", Arguments())).Output!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_module_resource_is_read_with_the_application_layer_applied()
    {
        const string layer = """
            { "id": "6f1d9c2a-3b4e-4d58-9a7c-1e2f3a4b5c6d", "kind": "layer", "formatVersion": 1, "target": "department-budgets/BudgetApproval",
              "changes": { "steps": [
                { "name": "review", "dueIn": "P3D", "outcomes": [{ "name": "reject", "remove": true }] },
                { "name": "archive", "after": "markRejected", "type": "end" } ] } }
            """;
        var path = Path.Combine(Folder, BudgetLayerFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, layer, CancellationToken);

        var call = await CallAsync(
            "read_module_resource", Arguments(("module", "department-budgets"), ("id", BudgetApprovalId)));

        Assert.False(call.IsError);
        Assert.Equal("modules/department-budgets/processes/budget-approval.json", call.Output!["file"]!.GetValue<string>());
        Assert.Equal(
            ["review", "markApproved", "markRejected", "archive", "done"],
            call.Output["content"]!["steps"]!.AsArray().Select(step => step!["name"]!.GetValue<string>()));
        Assert.Equal(BudgetLayerFile, call.Output["layer"]!["file"]!.GetValue<string>());
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, CancellationToken))),
            call.Output["layer"]!["version"]!.GetValue<string>());
        Assert.NotEmpty(call.Output["marks"]!.AsArray());
        Assert.Single(call.Output["removed"]!.AsArray());
    }

    [Fact]
    public async Task A_resource_over_one_mebibyte_is_read_page_by_page()
    {
        var draft = (await Drafts(_user!).CreateAsync(Revisions(_user!), CancellationToken)).Value!;
        var policy = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"),
            ["kind"] = "policy",
            ["name"] = "BigPolicy",
            ["description"] = string.Concat(Enumerable.Repeat("A \"big\" <policy> with Tiếng Việt, é and 😀. ", 30_000)),
        };
        Assert.Null((await Drafts(_user!).WriteAsync(draft.Id, policy, CancellationToken)).Refusal);
        var arguments = Arguments(("id", policy["id"]!.GetValue<string>()), ("draft", draft.Id));

        var whole = await ReadPagesAsync("read_resource", arguments);

        Assert.Equal("BigPolicy", whole["name"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(policy, whole["content"]));
        await AssertForeignCursorAsync("read_resource", arguments);
    }

    [Fact]
    public async Task A_module_resource_over_one_mebibyte_is_read_page_by_page()
    {
        var steps = new JsonArray(Enumerable.Range(1, 8).Select(number => (JsonNode)new JsonObject
        {
            ["name"] = "extra" + number,
            ["after"] = "markRejected",
            ["type"] = "operation",
            ["operation"] = "updateRecord",
            ["set"] = new JsonObject { ["status"] = "'" + new string((char)('a' + number), 150_000) + "'" },
            ["next"] = "done",
        }).ToArray());
        var layer = new JsonObject
        {
            ["id"] = "6f1d9c2a-3b4e-4d58-9a7c-1e2f3a4b5c6d",
            ["kind"] = "layer",
            ["formatVersion"] = 1,
            ["target"] = "department-budgets/BudgetApproval",
            ["changes"] = new JsonObject { ["steps"] = steps },
        };
        var path = Path.Combine(Folder, BudgetLayerFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, layer.ToJsonString(), CancellationToken);
        var arguments = Arguments(("module", "department-budgets"), ("id", BudgetApprovalId));

        var whole = await ReadPagesAsync("read_module_resource", arguments);

        Assert.Equal(BudgetLayerFile, whole["layer"]!["file"]!.GetValue<string>());
        Assert.Equal(
            ["review", "markApproved", "markRejected", "extra8", "extra7", "extra6", "extra5", "extra4", "extra3", "extra2", "extra1", "done"],
            whole["content"]!["steps"]!.AsArray().Select(step => step!["name"]!.GetValue<string>()));
        await AssertForeignCursorAsync("read_module_resource", arguments);
    }

    [Fact]
    public async Task A_cursor_on_an_output_that_gave_none_is_refused()
    {
        var process = StudioFolder.List(Folder).Single(file => file.Kind == "process" && file.Name == "PurchaseRequestApproval");

        Assert.Equal(
            McpForeignCursor,
            (await CallAsync("read_resource", Arguments(("id", process.Id.ToString("D")), ("cursor", "garbage")))).Error);
        Assert.Equal(McpForeignCursor, (await CallAsync("read_reference", """{"topic":"policy","cursor":"garbage"}""")).Error);
        Assert.Equal(InputMismatch, (await CallAsync("list_resources", Arguments(("cursor", "garbage")))).Error);
    }

    [Fact]
    public async Task A_refused_call_answers_the_title_of_the_authoring_api_and_no_path()
    {
        var unknownId = Guid.NewGuid().ToString("D");
        var drafts = Drafts(_user!);
        var own = (await drafts.CreateAsync(Revisions(_user!), CancellationToken)).Value!;
        var theirs = (await Drafts("someone-else").CreateAsync(Revisions("someone-else"), CancellationToken)).Value!;

        foreach (var tool in new[] { "list_resources", "read_names" })
        {
            Assert.Equal(NoApplication, (await CallAsync(tool, """{"application":"Nothing"}""")).Error);
            Assert.Equal(NoDraft, (await CallAsync(tool, Arguments(("draft", theirs.Id)))).Error);
            Assert.Equal(NoDraft, (await CallAsync(tool, Arguments(("draft", unknownId)))).Error);
            Assert.Equal(NoDraft, (await CallAsync(tool, Arguments(("draft", "not-a-guid")))).Error);
        }

        Assert.Equal(NoApplication, (await CallAsync("read_resource", $$"""{"application":"Nothing","id":"{{unknownId}}"}""")).Error);
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", unknownId)))).Error);
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", "not-a-guid")))).Error);
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", unknownId), ("draft", own.Id)))).Error);
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", "not-a-guid"), ("draft", own.Id)))).Error);
        var existing = StudioFolder.List(Folder)[0].Id.ToString("D");
        Assert.Equal(NoDraft, (await CallAsync("read_resource", Arguments(("id", existing), ("draft", theirs.Id)))).Error);
        Assert.Equal(NoDraft, (await CallAsync("read_resource", Arguments(("id", unknownId), ("draft", unknownId)))).Error);

        Assert.Equal(
            NoApplication,
            (await CallAsync("read_module_resource", $$"""{"application":"Nothing","module":"department-budgets","id":"{{BudgetApprovalId}}"}""")).Error);
        Assert.Equal(
            "The application uses no module with this id.",
            (await CallAsync("read_module_resource", Arguments(("module", "nope"), ("id", BudgetApprovalId)))).Error);
        Assert.Equal(
            "The module has no resource with this id.",
            (await CallAsync("read_module_resource", Arguments(("module", "department-budgets"), ("id", unknownId)))).Error);

        Assert.NotEmpty(_bodies);
        Assert.All(_bodies, body =>
        {
            Assert.DoesNotContain(_applications, body, StringComparison.Ordinal);
            Assert.DoesNotContain(_revisions, body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Input_that_breaks_the_input_schema_answers_a_tool_error_that_does_not_echo_the_input()
    {
        var secret = $"echo-{Guid.NewGuid():N}";
        foreach (var (tool, arguments) in new[]
        {
            ("list_resources", "{}"),
            ("list_resources", $$"""{"application":"{{Application}}","{{secret}}":"x"}"""),
            ("list_resources", $$"""{"application":"{{secret}}","draft":1}"""),
            ("read_resource", $$"""{"application":"{{Application}}"}"""),
            ("read_module_resource", $$"""{"application":"{{Application}}","module":"department-budgets","id":"{{BudgetApprovalId}}","draft":"{{secret}}"}"""),
            ("read_names", $$"""[ "{{secret}}" ]"""),
        })
        {
            var call = await CallAsync(tool, arguments);
            Assert.Equal(InputMismatch, call.Error);
        }

        Assert.All(_bodies, body => Assert.DoesNotContain(secret, body, StringComparison.Ordinal));
    }

    public async ValueTask InitializeAsync()
    {
        var authority = await keycloak.StartAsync();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        var platform = await database.CreateDatabaseAsync("mcp_read_tools", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(database.ConnectionString, CancellationToken);
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
        _user = new JwtSecurityTokenHandler().ReadJwtToken(_token).Subject;
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

    private static string Arguments(params (string Name, string Value)[] more) =>
        new JsonObject([
            new("application", Application),
            .. more.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Name, pair.Value)),
        ]).ToJsonString();

    private StudioDrafts Drafts(string user) =>
        StudioDrafts.For(_revisions, Folder, user, user, _factory!.Services.GetRequiredService<IStudioWriteLock>())!;

    private StudioRevisions Revisions(string user) =>
        StudioRevisions.For(_revisions, Folder, user, user, NullLogger<StudioRevisions>.Instance)!;

    private async Task<ToolAnswer> CallAsync(string tool, string arguments)
    {
        using var response = await McpTestCalls.PostAsync(
            _client!, Host, _token, McpTestCalls.ToolCall(tool, arguments), _session, Version, null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        _bodies.Add(text);
        var result = JsonNode.Parse(text)!["result"]!;
        return result["isError"]!.GetValue<bool>()
            ? new ToolAnswer(true, null, result["content"]![0]!["text"]!.GetValue<string>())
            : new ToolAnswer(false, result["structuredContent"], null);
    }

    // The pages of a tool call joined: each is at most one page of 1 MiB and there is more than one, the parts joined are the output's JSON text.
    private async Task<JsonNode> ReadPagesAsync(string tool, string arguments)
    {
        var parts = new List<string>();
        string? cursor = null;
        do
        {
            var call = await CallAsync(tool, cursor is null ? arguments : WithCursor(arguments, cursor));
            Assert.False(call.IsError, call.Error);
            Assert.True(Encoding.UTF8.GetByteCount(call.Output!.ToJsonString()) <= StudioDiffPages.PageBytes);
            parts.Add(call.Output["part"]!.GetValue<string>());
            cursor = call.Output["cursor"]?.GetValue<string>();
            Assert.True(parts.Count <= 10, "The pages do not end.");
        }
        while (cursor is not null);

        Assert.True(parts.Count > 1);
        return JsonNode.Parse(string.Concat(parts))!;
    }

    private async Task AssertForeignCursorAsync(string tool, string arguments)
    {
        Assert.Equal(McpForeignCursor, (await CallAsync(tool, WithCursor(arguments, "garbage"))).Error);
    }

    private static string WithCursor(string arguments, string cursor)
    {
        var withCursor = JsonNode.Parse(arguments)!.AsObject();
        withCursor["cursor"] = cursor;
        return withCursor.ToJsonString();
    }

    private sealed record ToolAnswer(bool IsError, JsonNode? Output, string? Error);
}
