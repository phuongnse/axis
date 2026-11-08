using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Data.Naming;
using Axis.Server.Mcp;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;

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

    private Uri? _authority;
    private string? _applications;

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

        var listed = await SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = listed["result"]!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(McpServer.Tools.Order(StringComparer.Ordinal), tools.Order(StringComparer.Ordinal));

        var failures = await McpToolGuard.LeakFailuresAsync(
            tools,
            new Dictionary<string, string> { ["list_applications"] = "{}" },
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

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        _applications = Directory.CreateTempSubdirectory("axis-mcp-output-").FullName;
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "StepApp"), Path.Combine(_applications, "step-app"));
    }

    public ValueTask DisposeAsync()
    {
        if (_applications is not null)
        {
            Directory.Delete(_applications, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
