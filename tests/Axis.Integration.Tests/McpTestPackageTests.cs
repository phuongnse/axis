using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Processes.Storage;
using Axis.Studio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>test_package</c> of the MCP server stores a package <c>test</c> publish request from the files of the person's draft, and the
/// publisher sends the package to <c>application-package-host</c> to test, see docs/reference/mcp-server.md#tools. A call with the
/// request then answers each test's outcome. The server is tenant <c>a</c> on a real PostgreSQL database. The host of the test
/// process runs the tests with a stub Trivy and the real pip, while the publisher's <c>PackageBuild:Python</c> names a missing command.
/// The Keycloak user <c>author</c> holds the publisher role, so a test makes it the developer role with <c>Studio:DeveloperRole</c> to
/// allow the tool, and any other role name to refuse it. The tests use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class McpTestPackageTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";
    private const string NoDraft = "The application has no draft with this id.";
    private const string NoRequest = "The application has no publish request with this id.";
    private const string NoPackage = "The application has no package with this id.";
    private const string NoDeveloper = "You do not hold the developer role.";
    private const string SwitchOff = "Application packages are off in this tenant.";

    private const string Manifest = """
        { "id": "tools", "version": "1.0.0", "label": "Tools", "language": "python", "adapter": "adapter.py", "lock": "requirements.txt",
          "operations": [ { "name": "sum", "type": "adapter.sum", "idempotent": true, "input": [], "output": [] } ] }
        """;

    private const string NoDependencies = "# No dependencies.\n";

    private const string ToolsTests = """
        import unittest


        class ToolsCase(unittest.TestCase):
            def test_passes(self):
                self.assertEqual(2, 1 + 1)

            def test_fails(self):
                self.assertEqual(1, 2)
        """;

    private static readonly string _missingWheel = $"./wheels/missing-1.0-py3-none-any.whl --hash=sha256:{new string('0', 64)}\n";

    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-test-package-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-test-package-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _token;
    private string? _session;
    private string? _user;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task A_package_with_a_passing_and_a_failing_test_ends_failed_with_both_outcomes_and_the_host_ran_it_while_run_scenarios_and_another_package_or_draft_do_not_answer_it()
    {
        await StartAsync(developerRole: "publisher", switchOn: true);
        var draft = await CreateDraftAsync();
        await WritePackageAsync(draft, NoDependencies);
        var other = await CreateDraftAsync();
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });

        var started = await CallAsync("test_package", Arguments(draft));

        Assert.False(started.IsError, started.Error);
        Assert.Equal(["diagnostics", "request", "status", "tests"], started.Output!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("queued", started.Output["status"]!.GetValue<string>());
        Assert.Empty(started.Output["diagnostics"]!.AsArray());
        Assert.Empty(started.Output["tests"]!.AsArray());
        var request = started.Output["request"]!.GetValue<string>();
        Assert.Equal(
            $"test|tenant|{draft}|tools",
            await ScalarAsync($"SELECT kind || '|' || target || '|' || draft_id || '|' || package FROM axis.publish_requests WHERE id = '{request}'"));

        await using var publisher = RunPublisher(host);
        var finished = await WaitForFinalAsync(draft, request);

        Assert.Equal(request, finished["request"]!.GetValue<string>());
        Assert.Equal("failed", finished["status"]!.GetValue<string>());
        Assert.Empty(finished["diagnostics"]!.AsArray());
        var tests = finished["tests"]!.AsArray();
        Assert.Equal(2, tests.Count);
        var passed = Assert.Single(tests, test => test!["name"]!.GetValue<string>() == "test_tools.ToolsCase.test_passes")!;
        Assert.Equal("passed", passed["outcome"]!.GetValue<string>());
        Assert.Null(passed["message"]);
        var failed = Assert.Single(tests, test => test!["name"]!.GetValue<string>() == "test_tools.ToolsCase.test_fails")!;
        Assert.Equal("failed", failed["outcome"]!.GetValue<string>());
        Assert.StartsWith("AssertionError", failed["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal([("tools", "1.0.0")], host.Tests());

        // Only a package test of this draft and this package is answered. Anything else is an unknown request.
        Assert.Equal(NoRequest, (await CallAsync("run_scenarios", $$"""{"application":"{{Application}}","draft":"{{draft}}","request":"{{request}}"}""")).Error);
        Assert.Equal(NoRequest, (await CallAsync("test_package", Arguments(draft, package: "other", request: request))).Error);
        Assert.Equal(NoRequest, (await CallAsync("test_package", Arguments(other, request: request))).Error);
        Assert.Equal(NoRequest, (await CallAsync("test_package", Arguments(draft, request: Guid.NewGuid().ToString("D")))).Error);
        Assert.Equal(NoRequest, (await CallAsync("test_package", Arguments(draft, request: "not-a-guid"))).Error);
        Assert.Equal(
            0L,
            await ScalarAsync("SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'tool' = 'test_package'"));
    }

    [Fact]
    public async Task A_package_whose_requirements_do_not_install_ends_failed_with_AXC0149_at_its_lock_file_and_no_tests()
    {
        await StartAsync(developerRole: "publisher", switchOn: true);
        var draft = await CreateDraftAsync();
        await WritePackageAsync(draft, _missingWheel);
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var started = await CallAsync("test_package", Arguments(draft));
        Assert.False(started.IsError, started.Error);

        await using var publisher = RunPublisher(host);
        var finished = await WaitForFinalAsync(draft, started.Output!["request"]!.GetValue<string>());

        Assert.Equal("failed", finished["status"]!.GetValue<string>());
        var diagnostic = Assert.Single(finished["diagnostics"]!.AsArray())!;
        Assert.Equal(DiagnosticCodes.ApplicationPackageBuildRefused, diagnostic["code"]!.GetValue<string>());
        Assert.Equal("packages/tools/requirements.txt", diagnostic["file"]!.GetValue<string>());
        Assert.Empty(finished["tests"]!.AsArray());
        Assert.Empty(host.Tests());
    }

    [Fact]
    public async Task A_person_without_the_developer_role_gets_a_tool_error_and_no_request_is_stored()
    {
        await StartAsync(developerRole: "developer", switchOn: true);
        var draft = await CreateDraftAsync();
        await WritePackageInDraftAsync(draft);

        Assert.Equal(NoDeveloper, (await CallAsync("test_package", Arguments(draft))).Error);
        Assert.Equal(NoDeveloper, (await CallAsync("test_package", Arguments(draft, request: Guid.NewGuid().ToString("D")))).Error);
        Assert.Equal(0L, await RequestCountAsync(draft));
    }

    [Fact]
    public async Task A_tenant_with_application_packages_off_gets_a_tool_error_and_no_request_is_stored()
    {
        await StartAsync(developerRole: "publisher", switchOn: false);
        var draft = await CreateDraftAsync();
        await WritePackageInDraftAsync(draft);

        Assert.Equal(SwitchOff, (await CallAsync("test_package", Arguments(draft))).Error);
        Assert.Equal(SwitchOff, (await CallAsync("test_package", Arguments(draft, request: Guid.NewGuid().ToString("D")))).Error);
        Assert.Equal(0L, await RequestCountAsync(draft));
    }

    [Fact]
    public async Task A_package_with_no_file_in_the_draft_answers_no_package_and_another_persons_or_an_unknown_draft_answers_no_draft_and_none_stores_a_request()
    {
        await StartAsync(developerRole: "publisher", switchOn: true);
        var draft = await CreateDraftAsync();
        var theirs = (await Drafts("someone-else").CreateAsync(Revisions("someone-else"), CancellationToken)).Value!;
        Assert.Null((await Drafts("someone-else").WritePackageFileAsync(theirs.Id, "packages/tools/axis-package.json", Encoding.UTF8.GetBytes(Manifest), null, false, CancellationToken)).Refusal);

        Assert.Equal(NoPackage, (await CallAsync("test_package", Arguments(draft))).Error);
        Assert.Equal(NoPackage, (await CallAsync("test_package", Arguments(draft, package: ".."))).Error);
        await WritePackageInDraftAsync(draft, package: "other");
        Assert.Equal(NoPackage, (await CallAsync("test_package", Arguments(draft))).Error);

        var unknown = Guid.NewGuid().ToString("D");
        foreach (var missing in new[] { theirs.Id, unknown, "not-a-guid" })
        {
            Assert.Equal(NoDraft, (await CallAsync("test_package", Arguments(missing))).Error);
            Assert.Equal(NoDraft, (await CallAsync("test_package", Arguments(missing, request: unknown))).Error);
        }

        Assert.Equal(0L, await RequestCountAsync(draft));
        Assert.Equal(0L, await RequestCountAsync(theirs.Id));
        Assert.Equal(0L, await RequestCountAsync(unknown));
    }

    public ValueTask InitializeAsync()
    {
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        return ValueTask.CompletedTask;
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

    // Starts the host of tenant a, whose developer role is the Keycloak role developerRole, and opens the MCP session of the user author.
    private async Task StartAsync(string developerRole, bool switchOn)
    {
        var authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("mcp_test_package", CancellationToken);
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
            builder.UseSetting("Studio:DeveloperRole", developerRole);
            builder.UseSetting("Studio:ApplicationsPath", _applications);
            builder.UseSetting("Studio:RevisionsPath", _revisions);
            if (switchOn)
            {
                // The configuration source has no switch, so the host turns it on for every tenant.
                builder.ConfigureTestServices(PackagesOnCatalog.Switch);
            }
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

    // The publisher serves a tenant only when its database holds every migration, the processes module's too.
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

        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(database.ConnectionString).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(database.ConnectionString, CancellationToken);
    }

    // The publisher of tenant a, with application packages on and the host of the test as its application runner.
    private StudioPublishTests.RunningPublisher RunPublisher(TestPackageHost host) =>
        StudioPublishTests.RunPublisher(
            "a",
            Host,
            database.ConnectionString,
            extra: new Dictionary<string, string?>
            {
                ["Extensions:ApplicationPackages"] = "true",
                ["Extensions:Runners:Application"] = host.Address,
                ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
            });

    // Writes the package tools of the draft with the tool of the MCP server: its manifest, adapter, lock file and test file.
    private async Task WritePackageAsync(string draft, string requirements)
    {
        (string Path, string Content)[] files =
        [
            ("axis-package.json", Manifest),
            ("adapter.py", "import sys\n"),
            ("requirements.txt", requirements),
            ("test_tools.py", ToolsTests),
        ];
        foreach (var (path, content) in files)
        {
            var written = await CallAsync("write_package_file", Arguments(draft, path: path, content: content));
            Assert.False(written.IsError, written.Error);
        }
    }

    // Writes the manifest of a package into the draft through the drafts, for a test whose tool call is refused.
    private async Task WritePackageInDraftAsync(string draft, string package = "tools") =>
        Assert.Null((await Drafts(_user!).WritePackageFileAsync(draft, $"packages/{package}/axis-package.json", Encoding.UTF8.GetBytes(Manifest), null, false, CancellationToken)).Refusal);

    // Polls the call with the request until the publisher has finished it.
    private async Task<JsonNode> WaitForFinalAsync(string draft, string request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            var answer = await CallAsync("test_package", Arguments(draft, request: request));
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

    // The arguments of a tool: the application, the draft, the package, and the request, the path and the content when given.
    private static string Arguments(string draft, string package = "tools", string? request = null, string? path = null, string? content = null) =>
        new JsonObject(new Dictionary<string, string?>
        {
            ["application"] = Application,
            ["draft"] = draft,
            ["package"] = package,
            ["request"] = request,
            ["path"] = path,
            ["content"] = content,
        }.Where(pair => pair.Value is not null).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value))).ToJsonString();

    private async Task<string> CreateDraftAsync() =>
        (await CallAsync("create_draft", $$"""{"application":"{{Application}}"}""")).Output!["id"]!.GetValue<string>();

    private async Task<long> RequestCountAsync(string draft) =>
        (long)(await ScalarAsync($"SELECT count(*) FROM axis.publish_requests WHERE draft_id = '{draft}'"))!;

    private StudioDrafts Drafts(string user) =>
        StudioDrafts.For(_revisions, Folder, user, user, _factory!.Services.GetRequiredService<IStudioWriteLock>())!;

    private StudioRevisions Revisions(string user) =>
        StudioRevisions.For(_revisions, Folder, user, user, NullLogger<StudioRevisions>.Instance)!;

    private Task<object?> ScalarAsync(string sql) => PostgreSqlServer.AdminScalarAsync(database.ConnectionString, sql, CancellationToken);

    private async Task<ToolAnswer> CallAsync(string tool, string arguments)
    {
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
