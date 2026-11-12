using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Processes.Storage;
using Axis.Server.Studio;
using Axis.Studio;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>accept_draft</c> of the MCP server queues an <c>accept</c> publish request of the person's draft only under the four rules of
/// docs/reference/authoring-requests.md#accepting, see docs/reference/mcp-server.md#tools. A broken rule is a tool error that stores
/// nothing, and a person without the author role never reaches the tool. No publisher runs, so a queued accept stays queued.
/// The server is tenant <c>a</c> on a real PostgreSQL database, and the folder holds the purchase-requests sample.
/// </summary>
public sealed class McpAcceptDraftTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";

    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-accept-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-accept-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private Uri? _authority;
    private string? _token;
    private string? _session;
    private string? _subject;
    private string? _platform;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task A_call_that_breaks_one_of_the_rules_is_a_tool_error_that_stores_no_accept_and_a_person_without_the_author_role_is_a_403()
    {
        var (draft, request) = await SeedReadyRequestAsync();

        await AssertRefusedAsync(draft, "The application does not allow auto-accept.", async () =>
        {
            await SetAsync($"UPDATE axis.application_settings SET auto_accept = false WHERE application_id = '{ApplicationId()}'");
            return () => SetAsync($"UPDATE axis.application_settings SET auto_accept = true WHERE application_id = '{ApplicationId()}'");
        });
        await AssertRefusedAsync(draft, "The authoring request did not ask for auto-accept.", async () =>
        {
            await SetAsync($"UPDATE axis.authoring_requests SET auto_accept = false WHERE id = '{request}'");
            return () => SetAsync($"UPDATE axis.authoring_requests SET auto_accept = true WHERE id = '{request}'");
        });

        // The last round is the newest check and the newest review, and the review must come after the check.
        const string NotPassed = "The last check and review of the authoring request did not both pass.";
        foreach (var (kind, outcome) in new[] { ("review", "findings"), ("check", "failed"), ("check", "passed") })
        {
            await AssertRefusedAsync(draft, NotPassed, async () =>
            {
                await SetAsync(
                    $"INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, outcome) VALUES ('{Guid.NewGuid()}', '{request}', 3, '{kind}', '{outcome}')");
                return () => SetAsync($"DELETE FROM axis.authoring_request_steps WHERE request_id = '{request}' AND ordinal = 3");
            });
        }

        const string NoReady = "No ready authoring request of yours has this draft.";
        await AssertRefusedAsync(draft, NoReady, async () =>
        {
            await SetAsync($"UPDATE axis.authoring_requests SET author = 'someone-else' WHERE id = '{request}'");
            return () => SetAsync($"UPDATE axis.authoring_requests SET author = '{_subject}' WHERE id = '{request}'");
        });
        await AssertRefusedAsync(draft, NoReady, async () =>
        {
            await SetAsync($"UPDATE axis.authoring_requests SET state = 'working' WHERE id = '{request}'");
            return () => SetAsync($"UPDATE axis.authoring_requests SET state = 'ready' WHERE id = '{request}'");
        });

        // A person without the author role is refused by the route before any tool runs.
        var requester = await McpTestCalls.RequestTokenAsync(_authority!, "requester", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret, CancellationToken);
        using var denied = await McpTestCalls.PostAsync(
            _client!, Host, requester, McpTestCalls.ToolCall("accept_draft", Arguments(("draft", draft))), _session, Version, null, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0L, await AcceptCountAsync(draft));
    }

    [Fact]
    public async Task When_all_four_rules_hold_the_call_stores_one_queued_accept_and_a_second_call_answers_it_and_a_call_with_the_request_reads_it()
    {
        var (draft, request) = await SeedReadyRequestAsync();

        var queued = await CallAsync("accept_draft", Arguments(("draft", draft)));

        Assert.False(queued.IsError, queued.Error);
        Assert.Equal(["diagnostics", "request", "status"], queued.Output!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("queued", queued.Output["status"]!.GetValue<string>());
        Assert.Empty(queued.Output["diagnostics"]!.AsArray());
        var accept = queued.Output["request"]!.GetValue<string>();
        Assert.Equal(
            $"accept|queued|{draft}|{Application}|{KeycloakFixture.McpClientId}|{_subject}",
            await ScalarAsync(
                $"SELECT kind || '|' || status || '|' || draft_id || '|' || folder || '|' || client || '|' || requested_by FROM axis.publish_requests WHERE id = '{accept}'"));
        Assert.Equal("ready", await ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{request}'"));
        Assert.Equal(1L, await AuditCountAsync(draft));

        // A second call answers the accept that is queued, stores nothing and writes no audit record.
        var again = await CallAsync("accept_draft", Arguments(("draft", draft)));
        Assert.False(again.IsError, again.Error);
        Assert.Equal(accept, again.Output!["request"]!.GetValue<string>());
        Assert.Equal(1L, await AcceptCountAsync(draft));
        Assert.Equal(1L, await AuditCountAsync(draft));

        // A call with the request reads that accept, and an id that is not an accept of this draft by this person is unknown.
        var read = await CallAsync("accept_draft", Arguments(("draft", draft), ("request", accept)));
        Assert.False(read.IsError, read.Error);
        Assert.Equal(accept, read.Output!["request"]!.GetValue<string>());
        Assert.Equal("queued", read.Output["status"]!.GetValue<string>());
        Assert.Empty(read.Output["diagnostics"]!.AsArray());
        Assert.Equal(
            StudioPublishEndpoints.NoRequest,
            (await CallAsync("accept_draft", Arguments(("draft", draft), ("request", Guid.NewGuid().ToString("D"))))).Error);
        await SetAsync($"UPDATE axis.publish_requests SET requested_by = 'someone-else' WHERE id = '{accept}'");
        Assert.Equal(StudioPublishEndpoints.NoRequest, (await CallAsync("accept_draft", Arguments(("draft", draft), ("request", accept)))).Error);
        Assert.Equal(1L, await AuditCountAsync(draft));
    }

    [Fact]
    public async Task A_call_with_the_request_reads_the_accept_after_the_publisher_has_removed_the_draft_and_an_id_of_another_draft_is_unknown()
    {
        var (draft, _) = await SeedReadyRequestAsync();
        var accept = (await CallAsync("accept_draft", Arguments(("draft", draft)))).Output!["request"]!.GetValue<string>();
        await SetAsync($"UPDATE axis.publish_requests SET status = 'succeeded' WHERE id = '{accept}'");
        var writeLock = new PlatformStudioWriteLock(NpgsqlDataSource.Create(_platform!), NullLogger<PlatformStudioWriteLock>.Instance);
        Assert.Null((await StudioDrafts.For(_revisions, Folder, _subject!, "Author", writeLock)!.DropAsync(draft, CancellationToken)).Refusal);

        // The draft is gone, as a successful accept leaves it, and the accept is still read.
        var read = await CallAsync("accept_draft", Arguments(("draft", draft), ("request", accept)));
        Assert.False(read.IsError, read.Error);
        Assert.Equal(accept, read.Output!["request"]!.GetValue<string>());
        Assert.Equal("succeeded", read.Output["status"]!.GetValue<string>());

        // Without the request the call still needs the draft, and a request of another draft is unknown.
        Assert.Equal(StudioRevisionEndpoints.NoDraft, (await CallAsync("accept_draft", Arguments(("draft", draft)))).Error);
        var other = Guid.NewGuid().ToString("D");
        Assert.Equal(StudioPublishEndpoints.NoRequest, (await CallAsync("accept_draft", Arguments(("draft", other), ("request", accept)))).Error);
        Assert.Equal(StudioRevisionEndpoints.NoDraft, (await CallAsync("accept_draft", Arguments(("draft", "not-a-draft"), ("request", accept)))).Error);
    }

    public async ValueTask InitializeAsync()
    {
        var authority = await keycloak.StartAsync();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        var platform = _platform = await database.CreateDatabaseAsync("mcp_accept_draft", CancellationToken);
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
        _subject = new JwtSecurityTokenHandler().ReadJwtToken(_token).Subject;
        _authority = authority;
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

    // A draft of the person, the application's auto-accept on, and a ready authoring request of the person that asked for it and whose
    // last check and review passed, in that order. Answers the draft and the request.
    private async Task<(string Draft, Guid Request)> SeedReadyRequestAsync()
    {
        var draft = (await CallAsync("create_draft", Arguments())).Output!["id"]!.GetValue<string>();
        var request = Guid.NewGuid();
        await SetAsync($"INSERT INTO axis.application_settings (application_id, auto_accept) VALUES ('{ApplicationId()}', true) ON CONFLICT (application_id) DO UPDATE SET auto_accept = true");
        await SetAsync(
            $"INSERT INTO axis.authoring_requests (id, application_id, author, text, state, draft_id, auto_accept) VALUES ('{request}', '{ApplicationId()}', '{_subject}', 'Add a field.', 'ready', '{draft}', true)");
        await SetAsync(
            $"INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, outcome) VALUES ('{Guid.NewGuid()}', '{request}', 1, 'check', 'passed'), ('{Guid.NewGuid()}', '{request}', 2, 'review', 'passed')");
        return (draft, request);
    }

    // Breaks one rule, expects the tool error and no stored accept, then restores the rule.
    private async Task AssertRefusedAsync(string draft, string error, Func<Task<Func<Task>>> breakRule)
    {
        var restore = await breakRule();
        var refused = await CallAsync("accept_draft", Arguments(("draft", draft)));
        Assert.True(refused.IsError);
        Assert.Equal(error, refused.Error);
        Assert.Equal(0L, await AcceptCountAsync(draft));
        await restore();
    }

    private Guid ApplicationId() => StudioFolder.ReadManifest(Folder)!.Id!.Value;

    private async Task<long> AcceptCountAsync(string draft) =>
        (long)(await ScalarAsync($"SELECT count(*) FROM axis.publish_requests WHERE kind = 'accept' AND draft_id = '{draft}'"))!;

    private async Task<long> AuditCountAsync(string draft) =>
        (long)(await ScalarAsync(
            $"SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'tool' = 'accept_draft' AND details->>'draftId' = '{draft}'"))!;

    private async Task SetAsync(string sql) => await ScalarAsync(sql);

    private static string Arguments(params (string Name, string Value)[] more) =>
        new JsonObject([
            new("application", Application),
            .. more.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Name, pair.Value)),
        ]).ToJsonString();

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
