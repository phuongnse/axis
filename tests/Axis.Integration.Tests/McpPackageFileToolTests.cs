using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
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
/// The package file tools of the MCP server, <c>list_package_files</c>, <c>read_package_file</c>, <c>write_package_file</c> and
/// <c>delete_package_file</c>, see docs/reference/mcp-server.md#tools. The server is tenant <c>a</c> on a real PostgreSQL database,
/// because a draft holds the folder's write lock, which lives in the platform database, and each write call writes an audit record
/// to the tenant database. The Keycloak user <c>author</c> holds the author and publisher roles, so a test makes the publisher role
/// the developer role with <c>Studio:DeveloperRole</c> to allow the write tools, and any other role name to refuse them. A test
/// starts its own host with <see cref="StartAsync"/>.
/// </summary>
public sealed class McpPackageFileToolTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string Application = "PurchaseRequests";
    private const string NoDraft = "The application has no draft with this id.";
    private const string NoPackageFile = "The application has no package file at this path.";
    private const string NoDeveloper = "You do not hold the developer role.";
    private const string SwitchOff = "Application packages are off in this tenant.";
    private const string Adapter = "packages/x/adapter.py";

    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-package-files-").FullName;
    private readonly string _revisions = Directory.CreateTempSubdirectory("axis-mcp-package-files-revisions-").FullName;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _token;
    private string? _session;
    private string? _user;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(_applications, "purchase-requests");

    [Fact]
    public async Task A_developer_writes_reads_lists_and_deletes_a_package_file_in_a_draft_and_the_working_folder_never_gains_it()
    {
        await StartAsync(developerRole: "publisher", switchOn: true);
        var draft = await CreateDraftAsync();
        var workingFiles = WorkingFiles();

        var written = await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('v1')\n"));
        Assert.False(written.IsError, written.Error);
        Assert.Equal(Hash("print('v1')\n"), written.Output!["version"]!.GetValue<string>());
        Assert.Equal(JsonValueKind.Array, written.Output["diagnostics"]!.GetValueKind());

        var read = await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"));
        Assert.False(read.IsError, read.Error);
        Assert.Equal("print('v1')\n", read.Output!["content"]!.GetValue<string>());
        Assert.Equal(Hash("print('v1')\n"), read.Output["version"]!.GetValue<string>());
        Assert.Equal("x", read.Output["package"]!.GetValue<string>());
        Assert.Equal("adapter.py", read.Output["path"]!.GetValue<string>());

        var listed = await CallAsync("list_package_files", Arguments(draft));
        var item = Assert.Single(listed.Output!["items"]!.AsArray());
        Assert.Equal(("x", "adapter.py", Hash("print('v1')\n")), (item!["package"]!.GetValue<string>(), item["path"]!.GetValue<string>(), item["version"]!.GetValue<string>()));

        // A second write replaces the file without a version.
        Assert.False((await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('v2')\n"))).IsError);
        Assert.Equal("print('v2')\n", (await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"))).Output!["content"]!.GetValue<string>());

        // The working folder holds no such file, in the folder or for the tools that read it.
        Assert.Equal(workingFiles, WorkingFiles());
        Assert.False(File.Exists(Path.Combine(Folder, "packages", "x", "adapter.py")));
        Assert.Equal(NoPackageFile, (await CallAsync("read_package_file", Arguments(null, "x", "adapter.py"))).Error);
        Assert.Empty((await CallAsync("list_package_files", Arguments(null))).Output!["items"]!.AsArray());

        var deleted = await CallAsync("delete_package_file", Arguments(draft, "x", "adapter.py"));
        Assert.False(deleted.IsError, deleted.Error);
        Assert.Equal(JsonValueKind.Array, deleted.Output!["diagnostics"]!.GetValueKind());
        Assert.Empty((await CallAsync("list_package_files", Arguments(draft))).Output!["items"]!.AsArray());
        Assert.Equal(NoPackageFile, (await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"))).Error);
        Assert.Equal(NoPackageFile, (await CallAsync("delete_package_file", Arguments(draft, "x", "adapter.py"))).Error);
        Assert.Equal(
            "The package or the path is not a plain file path under packages/.",
            (await CallAsync("write_package_file", Arguments(draft, "x", "../escape.py", "x"))).Error);
        Assert.Equal(workingFiles, WorkingFiles());
    }

    [Fact]
    public async Task A_write_or_delete_without_the_developer_role_is_a_tool_error_and_the_draft_is_unchanged_while_the_reads_work()
    {
        await StartAsync(developerRole: "developer", switchOn: true);
        var draft = await CreateDraftAsync();
        Assert.Null((await Drafts(_user!).WritePackageFileAsync(draft, Adapter, "print('kept')\n"u8.ToArray(), null, false, CancellationToken)).Refusal);
        var before = await DraftFilesAsync(draft);

        Assert.Equal(NoDeveloper, (await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('changed')\n"))).Error);
        Assert.Equal(NoDeveloper, (await CallAsync("write_package_file", Arguments(draft, "x", "other.py", "print('new')\n"))).Error);
        Assert.Equal(NoDeveloper, (await CallAsync("delete_package_file", Arguments(draft, "x", "adapter.py"))).Error);
        Assert.Equal(before, await DraftFilesAsync(draft));

        Assert.Equal("print('kept')\n", (await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"))).Output!["content"]!.GetValue<string>());
        Assert.Single((await CallAsync("list_package_files", Arguments(draft))).Output!["items"]!.AsArray());
        Assert.Equal(0L, await AuditCountAsync(draft, null));
    }

    [Fact]
    public async Task A_write_or_delete_in_a_tenant_with_application_packages_off_is_a_tool_error_and_the_draft_is_unchanged()
    {
        await StartAsync(developerRole: "publisher", switchOn: false);
        var draft = await CreateDraftAsync();
        Assert.Null((await Drafts(_user!).WritePackageFileAsync(draft, Adapter, "print('kept')\n"u8.ToArray(), null, false, CancellationToken)).Refusal);
        var before = await DraftFilesAsync(draft);

        Assert.Equal(SwitchOff, (await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('changed')\n"))).Error);
        Assert.Equal(SwitchOff, (await CallAsync("delete_package_file", Arguments(draft, "x", "adapter.py"))).Error);
        Assert.Equal(before, await DraftFilesAsync(draft));
        Assert.Equal("print('kept')\n", (await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"))).Output!["content"]!.GetValue<string>());
        Assert.Equal(0L, await AuditCountAsync(draft, null));
    }

    [Fact]
    public async Task Each_successful_write_or_delete_adds_one_audit_record_with_no_resource_id_and_a_refused_call_adds_none()
    {
        await StartAsync(developerRole: "publisher", switchOn: true);
        var draft = await CreateDraftAsync();

        Assert.False((await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('v1')\n"))).IsError);
        Assert.Equal(1L, await AuditCountAsync(draft, "write_package_file"));
        Assert.False((await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('v2')\n"))).IsError);
        Assert.Equal(2L, await AuditCountAsync(draft, "write_package_file"));

        // Reads and refused calls add none.
        Assert.False((await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"))).IsError);
        Assert.False((await CallAsync("list_package_files", Arguments(draft))).IsError);
        Assert.True((await CallAsync("write_package_file", Arguments(draft, "x", "../escape.py", "x"))).IsError);
        Assert.True((await CallAsync("delete_package_file", Arguments(draft, "x", "missing.py"))).IsError);
        Assert.Equal(2L, await AuditCountAsync(draft, null));

        Assert.False((await CallAsync("delete_package_file", Arguments(draft, "x", "adapter.py"))).IsError);
        Assert.Equal(1L, await AuditCountAsync(draft, "delete_package_file"));
        Assert.Equal(3L, await AuditCountAsync(draft, null));

        // The records hold ids only: the resource id is null, and no package, path or content is in them.
        const string Records = "SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'tool' <> 'create_draft'";
        Assert.Equal(3L, await CountAsync($"{Records} AND details->>'draftId' = '{draft}' AND jsonb_typeof(details->'resourceId') = 'null'"));
        Assert.Equal(
            0L,
            await CountAsync($"{Records} AND details->>'draftId' = '{draft}' AND (details::text LIKE '%adapter%' OR details::text LIKE '%print%' OR details::text LIKE '%packages%')"));
    }

    [Fact]
    public async Task Another_persons_draft_or_an_unknown_draft_answers_no_draft_and_nothing_changes()
    {
        await StartAsync(developerRole: "publisher", switchOn: true);
        var theirs = (await Drafts("someone-else").CreateAsync(Revisions("someone-else"), CancellationToken)).Value!;
        Assert.Null((await Drafts("someone-else").WritePackageFileAsync(theirs.Id, Adapter, "print('theirs')\n"u8.ToArray(), null, false, CancellationToken)).Refusal);
        var before = await Drafts("someone-else").ListPackageFilesAsync(theirs.Id, CancellationToken);
        var workingFiles = WorkingFiles();
        var records = await PersonsAuditCountAsync();

        foreach (var draft in new[] { theirs.Id, Guid.NewGuid().ToString("D"), "not-a-guid" })
        {
            Assert.Equal(NoDraft, (await CallAsync("write_package_file", Arguments(draft, "x", "adapter.py", "print('mine')\n"))).Error);
            Assert.Equal(NoDraft, (await CallAsync("delete_package_file", Arguments(draft, "x", "adapter.py"))).Error);
            Assert.Equal(NoDraft, (await CallAsync("read_package_file", Arguments(draft, "x", "adapter.py"))).Error);
            Assert.Equal(NoDraft, (await CallAsync("list_package_files", Arguments(draft))).Error);
        }

        Assert.Equal(before.Value, (await Drafts("someone-else").ListPackageFilesAsync(theirs.Id, CancellationToken)).Value);
        Assert.Equal(
            "print('theirs')\n"u8.ToArray(),
            (await Drafts("someone-else").ReadPackageFileAsync(theirs.Id, Adapter, CancellationToken)).Value);
        Assert.Equal(workingFiles, WorkingFiles());
        Assert.Equal(records, await PersonsAuditCountAsync());
    }

    public ValueTask InitializeAsync()
    {
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        return ValueTask.CompletedTask;
    }

    // Starts the host of tenant a, whose developer role is the Keycloak role developerRole, and opens the MCP session of the user author.
    private async Task StartAsync(string developerRole, bool switchOn)
    {
        var authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("mcp_package_files", CancellationToken);
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

    // The arguments of a package file tool: the application, a draft when given, and the package, the path and the content when given.
    private static string Arguments(string? draft, string? package = null, string? path = null, string? content = null) =>
        new JsonObject(new Dictionary<string, string?>
        {
            ["application"] = Application,
            ["draft"] = draft,
            ["package"] = package,
            ["path"] = path,
            ["content"] = content,
        }.Where(pair => pair.Value is not null).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value))).ToJsonString();

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private async Task<string> CreateDraftAsync() =>
        (await CallAsync("create_draft", $$"""{"application":"{{Application}}"}""")).Output!["id"]!.GetValue<string>();

    // The path and version of every file of the working folder, resources and package files alike.
    private List<string> WorkingFiles() =>
        [.. Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(file => $"{Path.GetRelativePath(Folder, file)} {Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))}")];

    // The package files of the draft as its branch holds them, with their versions.
    private async Task<List<string>> DraftFilesAsync(string draft) =>
        [.. (await Drafts(_user!).ListPackageFilesAsync(draft, CancellationToken)).Value!.Select(item => $"{item.Package}/{item.Path} {item.Version}")];

    private StudioDrafts Drafts(string user) =>
        StudioDrafts.For(_revisions, Folder, user, user, _factory!.Services.GetRequiredService<IStudioWriteLock>())!;

    private StudioRevisions Revisions(string user) =>
        StudioRevisions.For(_revisions, Folder, user, user, NullLogger<StudioRevisions>.Instance)!;

    // The mcp.toolCalled records of a draft, of one tool or of any.
    private Task<long> AuditCountAsync(string draft, string? tool) =>
        CountAsync(
            $"SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.toolCalled' AND details->>'draftId' = '{draft}'"
            + (tool is null ? "" : $" AND details->>'tool' = '{tool}'")
            + " AND details->>'tool' <> 'create_draft'");

    // Every mcp.toolCalled record of the calling person, which the tests of this class share a database to write.
    private Task<long> PersonsAuditCountAsync() =>
        CountAsync($"SELECT count(*) FROM axis.audit_records WHERE actor = '{_user}' AND action = 'mcp.toolCalled'");

    private async Task<long> CountAsync(string sql) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, sql, CancellationToken))!;

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
