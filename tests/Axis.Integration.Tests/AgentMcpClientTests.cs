using System.Net;
using System.Text.Json.Nodes;
using Axis.Agent;
using Axis.Data.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.AuthoringRealm;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// A step of the agent host calls the tenant's MCP server as the author through <see cref="AuthoringStepContext.Mcp"/>, see
/// <c>docs/reference/authoring-requests.md#the-agent-host</c>. The realm, the author and the server come from <see cref="AuthoringRealm"/>, and the
/// client reaches the server through its in-memory handler. A step lists the tools and reads the resources of the application with the token of a send. When the author has lost the
/// role, the server answers 403, and the request ends <c>failed</c> with the outcome <c>tokenEnded</c> and no token, see
/// <c>docs/reference/authoring-requests.md#acting-for-the-author</c>.
/// </summary>
public sealed class AgentMcpClientTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private static readonly AuthoringLoopOptions _options = new(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

    private readonly AuthoringRealm _realm = new(keycloak, database);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_step_lists_the_tools_and_reads_the_resources_of_the_application_as_the_author()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        JsonArray? tools = null;
        AuthorMcpToolResult? resources = null;
        var planning = new TestStep("planning", "plan", "awaitingApproval", async (context, token) =>
        {
            tools = await context.Mcp.ListToolsAsync(token);
            resources = await context.Mcp.CallToolAsync("list_resources", new JsonObject { ["application"] = "PurchaseRequests" }, token);
        });
        await using var host = BuildAgent(new LogCollector(), planning);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "awaitingApproval", "The host did not move the request to awaitingApproval.");
        await host.StopAsync(CancellationToken);

        Assert.Contains(tools!, tool => tool!["name"]!.GetValue<string>() == "list_resources");
        Assert.False(resources!.IsError);
        var manifest = resources.StructuredContent!["items"]!.AsArray().Single(item => item!["kind"]!.GetValue<string>() == "application")!;
        Assert.Equal("application.json", manifest["file"]!.GetValue<string>());
        Assert.Equal("1:plan:done:0:true", await StepsAsync(id));
    }

    [Fact]
    public async Task A_tool_call_of_an_author_who_lost_the_role_ends_the_request_failed_with_the_outcome_tokenEnded_and_no_token()
    {
        using var client = await _realm.SignInAsync();
        var id = await SendAsync(client);
        await _realm.RemoveAuthorRoleAsync();
        var reachedAfterCall = false;
        var logs = new LogCollector();
        var planning = new TestStep("planning", "plan", "awaitingApproval", async (context, token) =>
        {
            await context.Mcp.CallToolAsync("list_resources", new JsonObject { ["application"] = "PurchaseRequests" }, token);
            reachedAfterCall = true;
        });
        await using var host = BuildAgent(logs, planning);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(id) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:plan:tokenEnded:0:true", await StepsAsync(id));
        Assert.Equal("true", await ScalarAsync($"SELECT (refresh_token IS NULL)::text FROM axis.authoring_requests WHERE id = '{id}'"));
        Assert.False(reachedAfterCall);
        // The realm still refreshed the token, so it was the MCP server that refused the author.
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("refused the token of authoring request", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Contains("has ended, so the request stops as failed", StringComparison.Ordinal));
    }

    public async ValueTask InitializeAsync() => await _realm.InitializeAsync();

    public async ValueTask DisposeAsync() => await _realm.DisposeAsync();

    private static async Task<Guid> SendAsync(HttpClient client)
    {
        using var accepted = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var sent = await StudioPublishTests.ReadAsync(accepted);
        return Guid.Parse(sent.RootElement.GetProperty("id").GetString()!);
    }

    // The client reaches the server of the factory through its in-memory handler, as a host would reach it over the network.
    private WebApplication BuildAgent(LogCollector logs, TestStep step) =>
        AgentTestHost.Build(
            aiOn: true,
            _realm.AgentSettings(),
            logs,
            services =>
            {
                AgentTestHost.Steps(_options, step)(services);
                services.AddHttpClient(AuthorMcpClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _realm.Factory.Server.CreateHandler());
            });

    private async Task<string> ScalarAsync(string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private Task<string> StateAsync(Guid id) => ScalarAsync($"SELECT state FROM axis.authoring_requests WHERE id = '{id}'");

    private Task<string> StepsAsync(Guid id) =>
        ScalarAsync($"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{id}'");
}
