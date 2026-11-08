using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.Server.Mcp;
using Axis.Server.Studio;
using Axis.Server.Tests;
using Axis.Studio;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// Holds every tool that the MCP server offers to the rule in docs/reference/mcp-server.md#tools, see
/// <see cref="McpToolGuard"/>. The tools are read from <c>tools/list</c>, so a tool added later is called without anyone listing
/// it. The server is tenant <c>a</c> of <see cref="TaskApiFixture"/>, which holds a record, signed in through Keycloak at
/// <c>localhost</c>, with a <c>secret</c> and a <c>url</c> binding and the sign-in client secret set.
/// </summary>
public sealed class McpToolOutputTests(KeycloakFixture keycloak, TaskApiFixture fixture) : IClassFixture<TaskApiFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const string BudgetApprovalId = "94f1cc25-6a68-435f-9383-36fc43aae860";

    private Uri? _authority;
    private string? _applications;
    private string? _revisions;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_mcp_tool_answers_a_record_value_a_secret_a_binding_value_or_the_sign_in_client_secret()
    {
        var record = await fixture.InsertRequestAsync(TaskApiFixture.TenantA, null);
        var secret = $"guard-secret-{Guid.NewGuid():N}";
        var url = $"https://guard-binding-{Guid.NewGuid():N}.example.test/api";
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tenants:a:Hosts:1", Host);
            builder.UseSetting("Tenants:a:Oidc:Authority", _authority!.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting("Tenants:a:Bindings:GuardToken:Kind", "secret");
            builder.UseSetting("Tenants:a:Bindings:GuardToken:Value", secret);
            builder.UseSetting("Tenants:a:Bindings:GuardUrl:Kind", "url");
            builder.UseSetting("Tenants:a:Bindings:GuardUrl:Value", url);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", _applications);
            builder.UseSetting("Studio:RevisionsPath", _revisions);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{Host}"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var token = await McpTestCalls.RequestTokenAsync(_authority!, "author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret, CancellationToken);
        using var initialized = await McpTestCalls.PostAsync(client, Host, token, McpTestCalls.Initialize(Version), null, null, null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var session = Assert.Single(initialized.Headers.GetValues("Mcp-Session-Id"));

        async Task<JsonNode> SendAsync(string body)
        {
            using var response = await McpTestCalls.PostAsync(client, Host, token, body, session, Version, null, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        }

        // A draft of the person who holds the token, which the tools that write and check a draft are given.
        var subject = new JwtSecurityTokenHandler().ReadJwtToken(token).Subject;
        var stepApp = Path.Combine(_applications!, "step-app");
        var drafts = StudioDrafts.For(_revisions, stepApp, subject, subject, factory.Services.GetRequiredService<IStudioWriteLock>())!;
        var draft = (await drafts.CreateAsync(
            StudioRevisions.For(_revisions, stepApp, subject, subject, NullLogger<StudioRevisions>.Instance)!, CancellationToken)).Value!.Id;
        var written = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"),
            ["kind"] = "role",
            ["name"] = "guardwriter",
            ["formatVersion"] = 1,
            ["label"] = new JsonObject { ["textKey"] = "role.guardwriter" },
        };

        var listed = await SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = listed["result"]!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(McpServer.Tools.Order(StringComparer.Ordinal), tools.Order(StringComparer.Ordinal));

        var failures = await McpToolGuard.LeakFailuresAsync(
            tools,
            new Dictionary<string, string>
            {
                ["list_applications"] = "{}",
                ["read_reference"] = """{"topic":"entity"}""",
                ["list_resources"] = """{"application":"StepApp"}""",
                ["read_resource"] = $$"""{"application":"StepApp","id":"{{StepAppManifestId()}}"}""",
                ["read_module_resource"] = $$"""{"application":"PurchaseRequests","module":"department-budgets","id":"{{BudgetApprovalId}}"}""",
                ["read_names"] = """{"application":"StepApp"}""",
                ["create_draft"] = """{"application":"StepApp"}""",
                ["write_resource"] = new JsonObject { ["application"] = "StepApp", ["draft"] = draft, ["content"] = written }.ToJsonString(),
                ["delete_resource"] = $$"""{"application":"StepApp","draft":"{{draft}}","id":"{{FixtureId("roles", "auditor.json")}}"}""",
                ["check_expression"] = $$"""{"application":"StepApp","draft":"{{draft}}","id":"{{FixtureId("rules", "needs-review.json")}}","path":"/expression","expression":"total >= 500"}""",
            },
            (tool, arguments) => SendAsync(McpTestCalls.ToolCall(tool, arguments)),
            [TaskApiFixture.RequestTitle(record), secret, url, KeycloakFixture.ClientSecret],
            CancellationToken);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // The seeds are live: the tool reads the applications of the folder, and the database holds the record.
        var applications = await SendAsync(McpTestCalls.ToolCall("list_applications", "{}"));
        Assert.Contains(
            applications["result"]!["structuredContent"]!["items"]!.AsArray(),
            item => item!["name"]!.GetValue<string>() == "StepApp");
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        Assert.Equal(
            1L,
            await fixture.ScalarAsync(
                TaskApiFixture.TenantA,
                $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(request.Id))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id",
                ("id", record)));
    }

    // The id of the StepApp manifest, which is a resource of the folder.
    private static string StepAppManifestId() => FixtureId("application.json");

    // The id of a resource file of the StepApp fixture, such as roles/auditor.json.
    private static string FixtureId(params string[] path)
    {
        using var resource = JsonDocument.Parse(File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, "Fixtures", "StepApp", .. path])));
        return resource.RootElement.GetProperty("id").GetString()!;
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        _applications = Directory.CreateTempSubdirectory("axis-mcp-output-").FullName;
        _revisions = Directory.CreateTempSubdirectory("axis-mcp-output-revisions-").FullName;
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp"), Path.Combine(_applications, "step-app"));
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Path.Combine(_applications, "purchase-requests"));
    }

    public ValueTask DisposeAsync()
    {
        if (_applications is not null)
        {
            Directory.Delete(_applications, recursive: true);
        }

        if (_revisions is not null)
        {
            Directory.Delete(_revisions, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
