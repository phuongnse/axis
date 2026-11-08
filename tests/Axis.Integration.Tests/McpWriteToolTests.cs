using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Studio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The tools of the MCP server that change a draft, <c>create_draft</c>, <c>write_resource</c> and <c>delete_resource</c>, and
/// <c>check_expression</c>, see docs/reference/mcp-server.md#tools. The server is tenant <c>a</c> on a real PostgreSQL database,
/// because a draft holds the folder's write lock, which lives in the platform database, and each write call writes an audit
/// record to the tenant database. The folder holds the purchase-requests sample.
/// </summary>
public sealed class McpWriteToolTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";
    private const string ManifestId = "219102bc-864f-42ed-874a-c19eaee0d1ba";
    private const string NoDraft = "The application has no draft with this id.";
    private const string NoResource = "The application has no resource with this id.";
    private const string KindChange = "A patch may not change id or kind.";

    private readonly List<string> _bodies = [];
    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-write-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-write-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _token;
    private string? _session;
    private string? _user;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task An_invalid_resource_is_saved_in_the_draft_with_its_diagnostics_and_the_working_folder_does_not_change()
    {
        var before = FolderState();
        var created = await CallAsync("create_draft", Arguments());
        var draft = created.Output!["id"]!.GetValue<string>();
        Assert.Equal(40, created.Output["start"]!.GetValue<string>().Length);
        Assert.Equal(["id", "start"], created.Output.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));

        var id = Guid.NewGuid().ToString("D");
        var written = await CallAsync("write_resource", Arguments(("draft", draft)), Content(Entity(id, "BrokenThing", "nonsense")));

        Assert.False(written.IsError, written.Error);
        Assert.Equal(id, written.Output!["id"]!.GetValue<string>());
        Assert.Equal("entity", written.Output["kind"]!.GetValue<string>());
        Assert.Equal("BrokenThing", written.Output["name"]!.GetValue<string>());
        Assert.Equal("entities/broken-thing.json", written.Output["file"]!.GetValue<string>());
        Assert.NotEmpty(written.Output["version"]!.GetValue<string>());
        Assert.Contains(
            written.Output["diagnostics"]!.AsArray(),
            diagnostic => diagnostic!["resourceId"]?.GetValue<string>() == id && diagnostic["severity"]!.GetValue<string>() == "error");

        var read = await CallAsync("read_resource", Arguments(("id", id), ("draft", draft)));
        Assert.Equal("BrokenThing", read.Output!["name"]!.GetValue<string>());
        Assert.Equal("nonsense", read.Output["content"]!["fields"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", id)))).Error);
        Assert.Equal(before, FolderState());
        Assert.False(File.Exists(Path.Combine(Folder, "entities", "broken-thing.json")));

        // A second write to the same id replaces the file.
        var replaced = await CallAsync("write_resource", Arguments(("draft", draft)), Content(Entity(id, "BrokenThing", "text")));
        Assert.Equal("entities/broken-thing.json", replaced.Output!["file"]!.GetValue<string>());
        Assert.DoesNotContain(
            replaced.Output["diagnostics"]!.AsArray(),
            diagnostic => diagnostic!["resourceId"]?.GetValue<string>() == id && diagnostic["severity"]!.GetValue<string>() == "error");
        Assert.Equal(before, FolderState());
    }

    [Fact]
    public async Task A_write_that_the_authoring_api_refuses_answers_its_title_and_changes_nothing()
    {
        var draft = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        var role = Guid.NewGuid().ToString("D");
        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(Role(role, "reviewer")))).IsError);

        var kindChange = new JsonObject { ["id"] = role, ["kind"] = "rule", ["name"] = "reviewer", ["formatVersion"] = 1 };
        Assert.Equal(KindChange, (await CallAsync("write_resource", Arguments(("draft", draft)), Content(kindChange.ToJsonString()))).Error);
        Assert.Equal("role", (await CallAsync("read_resource", Arguments(("id", role), ("draft", draft)))).Output!["content"]!["kind"]!.GetValue<string>());

        Assert.Equal(
            "The name must be letters and digits, start with a letter and have at most 60 characters.",
            (await CallAsync("write_resource", Arguments(("draft", draft)), Content(Entity(Guid.NewGuid().ToString("D"), "bad name!", "text")))).Error);
        Assert.Equal(
            "The resource needs an id in the 8-4-4-4-12 hex form.",
            (await CallAsync("write_resource", Arguments(("draft", draft)), Content("""{ "kind": "entity", "name": "Thing" }"""))).Error);
        Assert.Equal(
            "The manifest cannot be created or deleted.",
            (await CallAsync("delete_resource", Arguments(("draft", draft), ("id", ManifestId)))).Error);
        Assert.Equal(NoResource, (await CallAsync("delete_resource", Arguments(("draft", draft), ("id", Guid.NewGuid().ToString("D"))))).Error);
        Assert.Equal(NoResource, (await CallAsync("delete_resource", Arguments(("draft", draft), ("id", "not-a-guid")))).Error);
        Assert.Equal(
            "The body is not a valid expression check.",
            (await CallAsync("check_expression", Arguments(("draft", draft), ("id", "not-a-guid"), ("path", "/expression"), ("expression", "1")))).Error);
        Assert.Equal(
            "The body is not a valid expression check.",
            (await CallAsync("check_expression", Arguments(("draft", draft), ("id", role), ("path", "expression"), ("expression", "1")))).Error);
        Assert.Equal(
            "The path holds no expression.",
            (await CallAsync("check_expression", Arguments(("draft", draft), ("id", role), ("path", "/name"), ("expression", "1")))).Error);

        // Only the one successful write was audited.
        Assert.Equal(1L, await AuditCountAsync(draft, "write_resource"));
        Assert.Equal(0L, await AuditCountAsync(draft, "delete_resource"));
        Assert.Equal(0L, await AuditCountAsync(draft, "check_expression"));
        Assert.NotEmpty(_bodies);
        Assert.All(_bodies, body =>
        {
            Assert.DoesNotContain(_applications, body, StringComparison.Ordinal);
            Assert.DoesNotContain(_revisions, body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_resource_is_removed_from_the_draft_and_the_working_folder_keeps_it()
    {
        var before = FolderState();
        var draft = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        var role = StudioFolder.List(Folder).First(file => file.Kind == "role" && file.Name == "operator").Id.ToString("D");

        var deleted = await CallAsync("delete_resource", Arguments(("draft", draft), ("id", role)));

        Assert.False(deleted.IsError, deleted.Error);
        Assert.NotNull(deleted.Output!["diagnostics"]);
        Assert.Equal(NoResource, (await CallAsync("read_resource", Arguments(("id", role), ("draft", draft)))).Error);
        Assert.Equal("operator", (await CallAsync("read_resource", Arguments(("id", role)))).Output!["name"]!.GetValue<string>());
        Assert.Equal(before, FolderState());
    }

    [Fact]
    public async Task An_expression_is_checked_in_a_resource_that_only_the_draft_holds_and_a_broken_one_has_a_diagnostic()
    {
        var before = FolderState();
        var draft = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        var rule = Guid.NewGuid().ToString("D");
        var content = new JsonObject
        {
            ["id"] = rule,
            ["kind"] = "rule",
            ["name"] = "DraftOnlyRule",
            ["formatVersion"] = 1,
            ["parameters"] = new JsonArray(),
            ["resultType"] = "decimal",
            ["expression"] = "1",
        };
        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(content.ToJsonString()))).IsError);
        var audits = await AuditCountAsync(draft, null);

        var typed = await CallAsync("check_expression", Arguments(("draft", draft), ("id", rule), ("path", "/expression"), ("expression", "1.5 * 2")));
        Assert.False(typed.IsError, typed.Error);
        Assert.Equal("decimal", typed.Output!["type"]!.GetValue<string>());
        Assert.Empty(typed.Output["diagnostics"]!.AsArray());

        var broken = await CallAsync("check_expression", Arguments(("draft", draft), ("id", rule), ("path", "/expression"), ("expression", "1 +")));
        Assert.False(broken.IsError, broken.Error);
        Assert.Null(broken.Output!["type"]);
        var diagnostic = Assert.Single(broken.Output["diagnostics"]!.AsArray())!;
        Assert.NotEmpty(diagnostic["code"]!.GetValue<string>());
        Assert.NotEmpty(diagnostic["message"]!.GetValue<string>());
        Assert.InRange(diagnostic["start"]!.GetValue<int>(), 0, 3);

        // The check writes nothing: the draft still holds the saved expression, the folder is as it was and no audit record is added.
        Assert.Equal("1", (await CallAsync("read_resource", Arguments(("id", rule), ("draft", draft)))).Output!["content"]!["expression"]!.GetValue<string>());
        Assert.Equal(NoResource, (await CallAsync("check_expression", Arguments(("draft", draft), ("id", Guid.NewGuid().ToString("D")), ("path", "/expression"), ("expression", "1")))).Error);
        Assert.Equal(audits, await AuditCountAsync(draft, null));
        Assert.Equal(before, FolderState());
    }

    [Fact]
    public async Task Each_write_call_writes_one_audit_record_that_holds_ids_only()
    {
        var created = await CallAsync("create_draft", Arguments());
        var draft = created.Output!["id"]!.GetValue<string>();
        Assert.Equal(1L, await AuditCountAsync(draft, "create_draft"));
        Assert.Equal(1L, await AuditCountAsync(draft, null));

        var id = Guid.NewGuid().ToString("D");
        Assert.False((await CallAsync("write_resource", Arguments(("draft", draft)), Content(Entity(id, "AuditedThing", "text")))).IsError);
        Assert.Equal(1L, await AuditCountAsync(draft, "write_resource"));
        Assert.Equal(2L, await AuditCountAsync(draft, null));

        Assert.False((await CallAsync("delete_resource", Arguments(("draft", draft), ("id", id)))).IsError);
        Assert.Equal(1L, await AuditCountAsync(draft, "delete_resource"));
        Assert.Equal(3L, await AuditCountAsync(draft, null));

        foreach (var (tool, resource) in new[] { ("create_draft", null), ("write_resource", id), ("delete_resource", id) })
        {
            var where = $"action = 'mcp.toolCalled' AND details->>'draftId' = '{draft}' AND details->>'tool' = '{tool}'";
            Assert.Equal(
                "clientId,draftId,resourceId,tool",
                await ScalarAsync($"SELECT string_agg(key, ',' ORDER BY key) FROM axis.audit_records, jsonb_object_keys(details) AS key WHERE {where}"));
            Assert.Equal(
                1L,
                await CountAsync(
                    $"SELECT count(*) FROM axis.audit_records WHERE {where} AND actor = '{_user}' AND application_id = '{ManifestId}'"
                    + $" AND details->>'clientId' = '{KeycloakFixture.McpClientId}'"
                    + (resource is null ? " AND details->'resourceId' = 'null'::jsonb" : $" AND details->>'resourceId' = '{resource}'")
                    + " AND entity_id IS NULL AND record_id IS NULL AND process_instance_id IS NULL"));
            Assert.Equal(
                0L,
                await CountAsync($"SELECT count(*) FROM axis.audit_records WHERE {where} AND details::text LIKE '%AuditedThing%'"));
        }
    }

    [Fact]
    public async Task Another_persons_draft_and_an_unknown_one_answer_no_draft_and_change_nothing_and_write_no_audit_record()
    {
        var theirs = (await Drafts("someone-else").CreateAsync(Revisions("someone-else"), CancellationToken)).Value!;
        var role = StudioFolder.List(Folder).First(file => file.Kind == "role").Id.ToString("D");
        var rule = Guid.NewGuid().ToString("D");
        var content = new JsonObject
        {
            ["id"] = rule,
            ["kind"] = "rule",
            ["name"] = "TheirRule",
            ["formatVersion"] = 1,
            ["parameters"] = new JsonArray(),
            ["resultType"] = "decimal",
            ["expression"] = "1",
        };
        var before = FolderState();
        var records = await PersonsAuditCountAsync();
        var theirFiles = (await Drafts("someone-else").FilesAsync(theirs.Id, CancellationToken)).Value!.Select(file => file.File).ToList();
        var unknown = Guid.NewGuid().ToString("D");

        foreach (var draft in new[] { theirs.Id, unknown, "not-a-guid" })
        {
            Assert.Equal(NoDraft, (await CallAsync("write_resource", Arguments(("draft", draft)), Content(content.ToJsonString()))).Error);
            Assert.Equal(NoDraft, (await CallAsync("delete_resource", Arguments(("draft", draft), ("id", role)))).Error);
            Assert.Equal(NoDraft, (await CallAsync("check_expression", Arguments(("draft", draft), ("id", role), ("path", "/expression"), ("expression", "1")))).Error);
        }

        Assert.Equal(theirFiles, (await Drafts("someone-else").FilesAsync(theirs.Id, CancellationToken)).Value!.Select(file => file.File).ToList());
        Assert.Empty((await Drafts("someone-else").DiffAsync(theirs.Id, null, CancellationToken)).Value!.Resources);
        Assert.Equal(before, FolderState());
        Assert.Equal(0L, await AuditCountAsync(theirs.Id, null));
        Assert.Equal(0L, await AuditCountAsync(unknown, null));
        Assert.Equal(records, await PersonsAuditCountAsync());
    }

    [Fact]
    public async Task Input_that_breaks_the_input_schema_answers_a_tool_error_that_does_not_echo_the_input()
    {
        var secret = $"echo-{Guid.NewGuid():N}";
        var draft = Guid.NewGuid().ToString("D");
        var records = await PersonsAuditCountAsync();
        foreach (var (tool, arguments) in new[]
        {
            ("create_draft", "{}"),
            ("create_draft", $$"""{"application":"{{Application}}","title":"{{secret}}"}"""),
            ("write_resource", $$"""{"application":"{{Application}}","draft":"{{draft}}"}"""),
            ("write_resource", $$"""{"application":"{{Application}}","draft":"{{draft}}","content":"{{secret}}"}"""),
            ("write_resource", $$"""{"application":"{{Application}}","draft":"{{draft}}","content":["{{secret}}"]}"""),
            ("delete_resource", $$"""{"application":"{{Application}}","id":"{{draft}}"}"""),
            ("check_expression", $$"""{"application":"{{Application}}","draft":"{{draft}}","id":"{{draft}}","path":1,"expression":"{{secret}}"}"""),
        })
        {
            Assert.Equal("The input does not match the tool's input schema.", (await CallAsync(tool, arguments)).Error);
        }

        Assert.All(_bodies, body => Assert.DoesNotContain(secret, body, StringComparison.Ordinal));
        Assert.Equal(records, await PersonsAuditCountAsync());
    }

    public async ValueTask InitializeAsync()
    {
        var authority = await keycloak.StartAsync();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        var platform = await database.CreateDatabaseAsync("mcp_write_tools", CancellationToken);
        await MigrateTenantDatabaseAsync();
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

    // The tenant database holds the audit records, so it has the data schema, and the runtime role may write to it.
    private async Task MigrateTenantDatabaseAsync()
    {
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(database.ConnectionString, CancellationToken);
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

    // The content argument is a JSON object, not a string, so it is added to the arguments as the object.
    private static (string Name, string Value) Content(string json) => ("content", json);

    private static string Entity(string id, string name, string fieldType) =>
        new JsonObject
        {
            ["id"] = id,
            ["kind"] = "entity",
            ["name"] = name,
            ["formatVersion"] = 1,
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = fieldType }),
        }.ToJsonString();

    private static string Role(string id, string name) =>
        new JsonObject { ["id"] = id, ["kind"] = "role", ["name"] = name, ["formatVersion"] = 1, ["label"] = new JsonObject { ["textKey"] = "role.employee" } }.ToJsonString();

    // The id and version of every resource of the working folder.
    private List<string> FolderState() => [.. StudioFolder.List(Folder).Select(file => $"{file.Id:D} {file.Version}")];

    private StudioDrafts Drafts(string user) =>
        StudioDrafts.For(_revisions, Folder, user, user, _factory!.Services.GetRequiredService<IStudioWriteLock>())!;

    private StudioRevisions Revisions(string user) =>
        StudioRevisions.For(_revisions, Folder, user, user, NullLogger<StudioRevisions>.Instance)!;

    // The mcp.toolCalled records of a draft, of one tool or of any.
    private Task<long> AuditCountAsync(string draft, string? tool) =>
        CountAsync(
            $"SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'draftId' = '{draft}'"
            + (tool is null ? "" : $" AND details->>'tool' = '{tool}'"));

    // Every mcp.toolCalled record of the calling person, which the tests of this class share a database to write.
    private Task<long> PersonsAuditCountAsync() =>
        CountAsync($"SELECT count(*) FROM axis.audit_records WHERE actor = '{_user}' AND action = 'mcp.toolCalled'");

    private async Task<long> CountAsync(string sql) => (long)(await ScalarAsync(sql))!;

    private Task<object?> ScalarAsync(string sql) => PostgreSqlServer.AdminScalarAsync(database.ConnectionString, sql, CancellationToken);

    // A tool call, whose arguments are the application and the properties given, with a content argument sent as a JSON object.
    private async Task<ToolAnswer> CallAsync(string tool, string arguments, (string Name, string Value)? content = null)
    {
        if (content is { } given)
        {
            var node = JsonNode.Parse(arguments)!.AsObject();
            node[given.Name] = JsonNode.Parse(given.Value);
            arguments = node.ToJsonString();
        }

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

    private sealed record ToolAnswer(bool IsError, JsonNode? Output, string? Error);
}
