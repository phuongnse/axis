using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>cell operator client add</c> makes a client of the operator realm in a real Keycloak. The client signs in with the client
/// credentials grant and its token holds the operator role. A second run keeps the one client and its secret and prints no secret. A
/// missing realm fails the step with exit 1. Each run writes one platform audit record with no tenant that never holds the secret.
/// </summary>
public sealed class AdminCellOperatorClientTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Command = "cell operator client add";
    private const string Role = "cell-operator";
    private const string ClientName = "control-plane";

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
    public async Task The_client_signs_in_with_the_role_and_a_second_run_keeps_the_one_client_and_its_secret()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var invite = await scenario.RunAsync(["cell", "operator", "invite", $"{Guid.NewGuid():N}@example.test"], identityProvider: _handler, extra: Settings());
        Assert.Equal(0, invite.Exit);

        var first = await scenario.RunAsync(AddArgs, identityProvider: _handler, extra: Settings());

        Assert.Equal(0, first.Exit);
        Assert.Equal(2, first.Output.Length);
        Assert.Equal($"{_realm} client ok", first.Output[0]);
        var prefix = $"{_realm} client-secret ";
        Assert.StartsWith(prefix, first.Output[1], StringComparison.Ordinal);
        var secret = first.Output[1][prefix.Length..];
        Assert.Equal(64, secret.Length);
        Assert.Contains(await TokenRolesAsync(secret), role => role == Role);

        var second = await scenario.RunAsync(AddArgs, identityProvider: _handler, extra: Settings());

        Assert.Equal(0, second.Exit);
        Assert.Equal([$"{_realm} client ok"], second.Output);
        Assert.DoesNotContain(second.Output.Concat(second.Error), line => line.Contains("client-secret", StringComparison.Ordinal));
        Assert.Single((await GetJsonAsync($"admin/realms/{_realm}/clients?clientId={ClientName}")).EnumerateArray());
        Assert.Contains(await TokenRolesAsync(secret), role => role == Role);

        var records = (await scenario.AuditRecordsAsync()).Where(record => record.Command == Command).ToList();
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Null(record.TenantId);
            Assert.Equal([$"realm: {_realm}", $"role: {Role}", $"client: {ClientName}"], record.Changed);
            Assert.DoesNotContain(secret, record.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_second_run_gives_the_role_again_and_resets_the_settings_of_the_client()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        Assert.Equal(0, (await scenario.RunAsync(["cell", "operator", "invite", $"{Guid.NewGuid():N}@example.test"], identityProvider: _handler, extra: Settings())).Exit);
        var first = await scenario.RunAsync(AddArgs, identityProvider: _handler, extra: Settings());
        var secret = first.Output[1][(first.Output[1].LastIndexOf(' ') + 1)..];
        var client = (await GetJsonAsync($"admin/realms/{_realm}/clients?clientId={ClientName}")).EnumerateArray().Single();
        var uuid = client.GetProperty("id").GetString()!;
        var user = await GetJsonAsync($"admin/realms/{_realm}/clients/{uuid}/service-account-user");
        var roleRepresentation = await GetJsonAsync($"admin/realms/{_realm}/roles/{Role}");
        using (var http = new HttpClient(_handler, disposeHandler: false))
        {
            using var removed = await SendAsync(
                http, await MasterTokenAsync(http), HttpMethod.Delete, $"admin/realms/{_realm}/users/{user.GetProperty("id").GetString()}/role-mappings/realm", new[] { roleRepresentation });
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.DoesNotContain(await TokenRolesAsync(secret), role => role == Role);

        var second = await scenario.RunAsync(AddArgs, identityProvider: _handler, extra: Settings());

        Assert.Equal(0, second.Exit);
        Assert.Contains(await TokenRolesAsync(secret), role => role == Role);
    }

    [Fact]
    public async Task When_the_realm_does_not_exist_the_client_step_fails_with_exit_1()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);

        var result = await scenario.RunAsync(AddArgs, identityProvider: _handler, extra: Settings());

        Assert.Equal(1, result.Exit);
        var line = Assert.Single(result.Output);
        Assert.StartsWith($"{_realm} client failed:", line, StringComparison.Ordinal);
        Assert.Contains("cell operator invite", line, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Output, output => output.Contains("client-secret", StringComparison.Ordinal));
        var records = (await scenario.AuditRecordsAsync()).Where(record => record.Command == Command).ToList();
        Assert.Single(records);
    }

    private static string[] AddArgs => ["cell", "operator", "client", "add", ClientName];

    // The settings that the command needs: a cell and the real identity provider. It reads no mail settings.
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

    // The realm roles in the access token that the client gets with the client credentials grant.
    private async Task<string[]> TokenRolesAsync(string secret)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientName,
            ["client_secret"] = secret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, $"realms/{_realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var payload = body.GetProperty("access_token").GetString()!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
        return claims.TryGetProperty("realm_access", out var access)
            ? [.. access.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!)]
            : [];
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
