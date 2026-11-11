using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Admin;
using Axis.Agent;
using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// A request whose token for the agent host has ended stops as <c>failed</c> and loses its token, and a step that ends a request erases it,
/// see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>. The realm is made by <see cref="TenantRealm"/> in a real Keycloak.
/// A stored token that the realm refuses with <c>invalid_grant</c>, or none, fails the request when a step asks for the access token. A step that
/// moves a request to <c>accepted</c> or <c>failed</c> erases the token, and a step that moves it to <c>checking</c> keeps it.
/// </summary>
public sealed class AgentRequestTokenTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string PlatformSecret = "platform-secret-1";
    private const string AgentSecret = "agent-secret-1";
    private const string ActivationSecret = "activation-secret-1";
    private const string Tenant = "a";

    private static readonly AuthoringLoopOptions _options = new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100));

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly string _dataKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => new(_baseAddress, $"realms/{_realm}");

    [Fact]
    public async Task A_working_request_whose_stored_token_the_realm_refuses_ends_failed_with_no_token_and_the_step_outcome_tokenEnded()
    {
        var (connectionString, requestId) = await CreateRequestAsync("working", "not-a-refresh-token-4815162342");
        var logs = new LogCollector();
        var reachedAfterToken = false;
        var working = new TestStep("working", "work", "checking", async (context, token) =>
        {
            await context.AccessTokenAsync(token);
            reachedAfterToken = true;
        });
        await using var host = BuildAgent(connectionString, logs, working);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("true|true|true", await ScalarAsync(connectionString, $"SELECT (refresh_token IS NULL) || '|' || (finished_at IS NOT NULL) || '|' || (claimed_by IS NULL) FROM axis.authoring_requests WHERE id = '{requestId}'"));
        Assert.Equal("1:work:tokenEnded:0:true", await StepsAsync(connectionString, requestId));
        Assert.False(reachedAfterToken);
        Assert.Contains(logs.Entries, entry => entry.Contains("refused the token of authoring request", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Contains("has ended, so the request stops as failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("4815162342", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_working_request_with_no_stored_token_ends_failed_when_its_step_asks_for_the_access_token()
    {
        var (connectionString, requestId) = await CreateRequestAsync("working", null);
        var working = new TestStep("working", "work", "checking", (context, token) => context.AccessTokenAsync(token));
        await using var host = BuildAgent(connectionString, new LogCollector(), working);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == "failed", "The host did not stop the request as failed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal("1:work:tokenEnded:0:true", await StepsAsync(connectionString, requestId));
    }

    [Theory]
    [InlineData("accepted", false)]
    [InlineData("failed", false)]
    [InlineData("checking", true)]
    public async Task A_step_that_ends_a_request_erases_its_token_and_a_step_that_moves_it_on_keeps_the_token(string nextState, bool kept)
    {
        var (connectionString, requestId) = await CreateRequestAsync("reviewing", "a-refresh-token-1234567890");
        var reviewing = new TestStep("reviewing", "review", nextState);
        await using var host = BuildAgent(connectionString, new LogCollector(), reviewing);
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await StateAsync(connectionString, requestId) == nextState, $"The host did not move the request to {nextState}.");
        await host.StopAsync(CancellationToken);

        Assert.Equal(kept, await ScalarAsync(connectionString, $"SELECT (refresh_token IS NOT NULL)::text FROM axis.authoring_requests WHERE id = '{requestId}'") == "true");
        Assert.Equal("1:review:done:0:true", await StepsAsync(connectionString, requestId));
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, ActivationSecret, "users-secret-1", AgentSecret), new Dictionary<string, string>(), CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{_realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    // The token is encrypted with the tenant's data key for the request, as the server stores it, and written as the superuser of the container.
    private async Task<(string ConnectionString, Guid RequestId)> CreateRequestAsync(string state, string? refreshToken)
    {
        var connectionString = await database.CreateDatabaseAsync("agent_token", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        var requestId = Guid.CreateVersion7();
        await ScalarAsync(
            connectionString,
            $"INSERT INTO axis.authoring_requests (id, application_id, author, text, state) VALUES ('{requestId}', '{Guid.NewGuid()}', 'author-1', 'Add a field.', '{state}') RETURNING 1");
        if (refreshToken is not null)
        {
            var values = new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [$"Tenants:{Tenant}:DataKeys:1"] = _dataKey })
                .Build())));
            var stored = await values.EncryptTextAsync(AuthoringRequestTokens.Scope(Tenant, requestId), AuthoringRequestTokens.FieldName, refreshToken, CancellationToken);
            await ScalarAsync(connectionString, $"UPDATE axis.authoring_requests SET refresh_token = '\\x{Convert.ToHexString(stored)}'::bytea WHERE id = '{requestId}' RETURNING 1");
        }

        return (connectionString, requestId);
    }

    private WebApplication BuildAgent(string connectionString, LogCollector logs, params TestStep[] steps) =>
        AgentTestHost.Build(
            aiOn: true,
            AgentTestHost.TenantSettings(Tenant, $"{Tenant}.example.test", PostgreSqlServer.RuntimeConnectionString(connectionString), Authority, PlatformSecret, AgentSecret, _dataKey),
            logs,
            AgentTestHost.Steps(_options, steps));

    private static async Task<string> ScalarAsync(string connectionString, string sql) =>
        Convert.ToString(await PostgreSqlServer.AdminScalarAsync(connectionString, sql, CancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static Task<string> StateAsync(string connectionString, Guid requestId) =>
        ScalarAsync(connectionString, $"SELECT state FROM axis.authoring_requests WHERE id = '{requestId}'");

    private static Task<string> StepsAsync(string connectionString, Guid requestId) =>
        ScalarAsync(
            connectionString,
            $"SELECT string_agg(ordinal || ':' || kind || ':' || coalesce(outcome, '-') || ':' || tokens || ':' || (finished_at IS NOT NULL), ',' ORDER BY ordinal) FROM axis.authoring_request_steps WHERE request_id = '{requestId}'");

    private async Task<string> MasterTokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }
}
