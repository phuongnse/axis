using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>cell operator invite</c> makes the operator realm and invites the first operator in a real Keycloak and a real Mailpit. Two
/// runs leave one realm whose browser flow requires the authenticator code, which has no brokered identity provider and which has the
/// operator role. The invited account holds the role, has the actions that set a password and an authenticator and gets one email. Each
/// run writes one platform audit record with no tenant that never holds the email.
/// </summary>
public sealed class AdminCellOperatorInviteTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Command = "cell operator invite";
    private const string Role = "cell-operator";

    private readonly HttpClientHandler _handler = new();
    private readonly string _realm = $"op-{Guid.NewGuid():N}"[..20];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await MasterTokenAsync(http), HttpMethod.Delete, $"admin/realms/{_realm}");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _handler.Dispose();
    }

    [Fact]
    public async Task Two_runs_leave_one_realm_that_requires_the_authenticator_code_has_no_identity_provider_and_has_the_role()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var email = NewEmail();

        var first = await scenario.RunAsync(InviteArgs(email), identityProvider: _handler, extra: Settings());

        Assert.Equal(0, first.Exit);
        Assert.Equal([$"{_realm} realm ok", $"{_realm} account ok", $"{_realm} invitation ok"], first.Output);

        // A provider that someone added between the runs is removed by the second one.
        await AddIdentityProviderAsync();
        Assert.Single((await GetJsonAsync($"admin/realms/{_realm}/identity-provider/instances")).EnumerateArray());

        var second = await scenario.RunAsync(InviteArgs(email), identityProvider: _handler, extra: Settings());

        Assert.Equal(0, second.Exit);
        var realms = await GetJsonAsync("admin/realms");
        Assert.Single(realms.EnumerateArray(), realm => realm.GetProperty("realm").GetString() == _realm);
        Assert.Empty((await GetJsonAsync($"admin/realms/{_realm}/identity-provider/instances")).EnumerateArray());
        var executions = await GetJsonAsync($"admin/realms/{_realm}/authentication/flows/browser/executions");
        var otp = Assert.Single(executions.EnumerateArray(), execution => execution.TryGetProperty("providerId", out var id) && id.GetString() == "auth-otp-form");
        Assert.Equal("REQUIRED", otp.GetProperty("requirement").GetString());
        Assert.Equal(Role, (await GetJsonAsync($"admin/realms/{_realm}/roles/{Role}")).GetProperty("name").GetString());
        Assert.Empty((await GetJsonAsync($"admin/realms/{_realm}/clients?clientId=axis")).EnumerateArray());

        var records = await scenario.AuditRecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(Command, record.Command);
            Assert.Null(record.TenantId);
            Assert.Equal([$"realm: {_realm}", $"role: {Role}"], record.Changed);
            Assert.DoesNotContain(email, record.Text, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain(first.Output.Concat(first.Error).Concat(second.Output).Concat(second.Error), line => line.Contains(email, StringComparison.OrdinalIgnoreCase));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task The_invited_account_holds_the_role_has_the_required_actions_and_gets_one_email()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var email = NewEmail();

        var first = await scenario.RunAsync(InviteArgs(email), identityProvider: _handler, extra: Settings());

        Assert.Equal(0, first.Exit);
        var user = await OperatorAsync(email);
        var userId = user.GetProperty("id").GetString()!;
        var actions = user.GetProperty("requiredActions").EnumerateArray().Select(action => action.GetString()).ToList();
        Assert.Contains("UPDATE_PASSWORD", actions);
        Assert.Contains("CONFIGURE_TOTP", actions);
        var roles = await GetJsonAsync($"admin/realms/{_realm}/users/{userId}/role-mappings/realm");
        Assert.Contains(roles.EnumerateArray(), role => role.GetProperty("name").GetString() == Role);
        var message = Assert.Single(await InvitationFlow.WaitForMailsAsync(mailpit, email, 1));
        Assert.Contains("login-actions/action-token", message.Text, StringComparison.Ordinal);

        var second = await scenario.RunAsync(InviteArgs(email), identityProvider: _handler, extra: Settings());

        Assert.Equal(0, second.Exit);
        Assert.Equal(userId, (await OperatorAsync(email)).GetProperty("id").GetString());
        await scenario.AssertNoSecretsAsync();
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static string[] InviteArgs(string email) => ["cell", "operator", "invite", email];

    // The settings that the command needs: a cell, Development, which allows a mail server without TLS, the real identity provider and the mail server.
    private Dictionary<string, string?> Settings() => new()
    {
        ["Cell:Id"] = "vn-1",
        ["Cell:Region"] = "vn",
        ["Cell:TenantDomain"] = "vn.example.test",
        ["Cell:OperatorRealm"] = _realm,
        ["Cell:OperatorRole"] = Role,
        ["Tenancy:Source"] = "directory",
        ["DOTNET_ENVIRONMENT"] = "Development",
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
        ["Email:Smtp:Host"] = MailpitFixture.NetworkAlias,
        ["Email:Smtp:Port"] = $"{MailpitFixture.NetworkSmtpPort}",
        ["Email:Smtp:UseTls"] = "false",
        ["Email:From"] = "noreply@example.test",
    };

    private async Task<JsonElement> OperatorAsync(string email) =>
        (await GetJsonAsync($"admin/realms/{_realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray().Single();

    private async Task AddIdentityProviderAsync()
    {
        var provider = new JsonObject
        {
            ["alias"] = "corporate",
            ["providerId"] = "oidc",
            ["enabled"] = true,
            ["config"] = new JsonObject
            {
                ["authorizationUrl"] = "http://127.0.0.1:1/authorize",
                ["tokenUrl"] = "http://127.0.0.1:1/token",
                ["clientId"] = "axis",
                ["clientSecret"] = "secret",
            },
        };
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await MasterTokenAsync(http), HttpMethod.Post, $"admin/realms/{_realm}/identity-provider/instances", provider);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await MasterTokenAsync(http), HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<string> MasterTokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }
}
