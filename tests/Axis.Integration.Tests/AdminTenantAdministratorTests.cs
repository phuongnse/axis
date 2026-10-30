using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant create --administrator</c> invites the first administrator of a tenant in a real Keycloak and a real Mailpit. The
/// <c>administrator</c> step creates the invited account in the tenant's realm, gives it the administrator platform role, has the
/// realm email the link and writes the <c>user.invited</c> and <c>user.roleGiven</c> records, whose actor is the command and which
/// never hold the link. The person follows the link, sets a password and an authenticator, and signs in through the server. A
/// failed invitation leaves the tenant <c>provisioning</c>, and a rerun finds the account instead of creating a second one.
/// </summary>
public sealed class AdminTenantAdministratorTests(KeycloakFixture keycloak, MailpitFixture mailpit, PostgreSqlFixture database)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Command = "tenant create";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task The_invited_administrator_follows_the_link_signs_in_and_holds_the_administrator_role_and_the_records_hold_no_link()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var email = NewEmail();

        var created = await scenario.RunAsync(CreateArgs(id, email), identityProvider: _handler, extra: Settings(Mailpit()));

        Assert.Equal(0, created.Exit);
        Assert.Equal(["version", "administrator"], created.Output.Select(StepOf).TakeLast(2));
        Assert.Equal("active", (await scenario.RowAsync(id)).Status);
        Assert.DoesNotContain(created.Output.Concat(created.Error), line => line.Contains(email, StringComparison.OrdinalIgnoreCase));

        var userId = await RealmUserIdAsync(id, email);
        await using var connection = await TenantConnectionAsync(scenario, id);
        var invited = Assert.Single(await AuditAsync(connection, "user.invited"));
        Assert.Equal(Command, invited.Actor);
        Assert.Equal(userId, invited.Details["userId"]!.GetValue<string>());
        Assert.Equal(email, invited.Details["email"]!.GetValue<string>());
        var given = Assert.Single(await AuditAsync(connection, "user.roleGiven"));
        Assert.Equal(Command, given.Actor);
        Assert.Equal(userId, given.Details["userId"]!.GetValue<string>());
        Assert.Equal(UserInvitations.AdministratorKind, given.Details["role"]!.GetValue<string>());
        Assert.True(given.Details["platform"]!.GetValue<bool>());
        Assert.Equal(0L, await CountAsync(connection, "SELECT count(*) FROM axis.audit_records WHERE details::text LIKE '%action-token%' OR details::text LIKE '%key=%'"));
        Assert.Equal([new GivenRole(null, UserInvitations.AdministratorKind)], await GivenRoles.ListAsync(connection, userId, CancellationToken));

        var message = Assert.Single(await InvitationFlow.WaitForMailsAsync(mailpit, email, 1));
        var flow = new InvitationFlow();
        using var browser = new InvitationFlow.RealmBrowser();
        var password = Guid.NewGuid().ToString("N");
        var secret = await flow.SetPasswordAndAuthenticatorAsync(browser, InvitationFlow.LinkIn(message), password, homeLink: null);

        // The command names no return address, so the realm has none to accept yet. The test adds the server's own.
        await AllowReturnAddressAsync(id);
        await using var factory = ServerFactory(scenario);
        using var app = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        await flow.SignInAsync(app, email, password, secret);

        using var me = await app.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(userId, body.GetProperty("id").GetString());
        Assert.True(body.GetProperty("users").GetBoolean());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_failed_invitation_leaves_the_tenant_provisioning_and_a_rerun_uses_the_same_account_and_activates_it()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        await mailpit.SmtpHostAsync();
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var email = NewEmail();
        var unreachable = new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = "1",
            ["Email:Smtp:UseTls"] = "false",
            ["Email:From"] = "host@example.test",
        };

        var first = await scenario.RunAsync(CreateArgs(id, email), identityProvider: _handler, extra: Settings(unreachable));

        Assert.Equal(1, first.Exit);
        Assert.StartsWith($"{id} administrator failed", first.Output[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(first.Output.Concat(first.Error), line => line.Contains(email, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);
        Assert.Empty(await mailpit.MessagesToAsync(email, CancellationToken));

        var second = await scenario.RunAsync(CreateArgs(id, email), identityProvider: _handler, extra: Settings(Mailpit()));

        Assert.Equal(0, second.Exit);
        Assert.Equal($"{id} administrator ok", second.Output[^1]);
        Assert.Equal("active", (await scenario.RowAsync(id)).Status);
        var users = await GetJsonAsync($"admin/realms/{id}/users?email={Uri.EscapeDataString(email)}&exact=true");
        var userId = users.EnumerateArray().Single().GetProperty("id").GetString()!;
        await using var connection = await TenantConnectionAsync(scenario, id);
        Assert.Single(await AuditAsync(connection, "user.invited"));
        Assert.Single(await AuditAsync(connection, "user.roleGiven"));
        Assert.Equal([new GivenRole(null, UserInvitations.AdministratorKind)], await GivenRoles.ListAsync(connection, userId, CancellationToken));
        Assert.Single(await InvitationFlow.WaitForMailsAsync(mailpit, email, 1));
        await scenario.AssertNoSecretsAsync();
    }

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static string[] CreateArgs(string id, string email) => [.. AdminScenario.CreateArgs(id, "localhost"), "--administrator", email];

    // The word after the tenant id in a step line such as "acme version ok".
    private static string StepOf(string line) => line.Split(' ')[1];

    private static Dictionary<string, string?> Mailpit() => new()
    {
        ["Email:Smtp:Host"] = MailpitFixture.NetworkAlias,
        ["Email:Smtp:Port"] = $"{MailpitFixture.NetworkSmtpPort}",
        ["Email:Smtp:UseTls"] = "false",
        ["Email:From"] = "noreply@example.test",
    };

    // The settings that the command needs: the directory source, Development, which allows a mail server without TLS, and the real identity provider.
    private Dictionary<string, string?> Settings(Dictionary<string, string?> mail)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["IdentityProvider:Url"] = _baseAddress.ToString(),
            ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
            ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
        };
        foreach (var (name, value) in mail)
        {
            settings[name] = value;
        }

        return settings;
    }

    private static WebApplicationFactory<Program> ServerFactory(AdminScenario scenario) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(scenario.Platform);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Users:AdministratorRole", UserInvitations.AdministratorKind);
        });

    private static async Task<NpgsqlConnection> TenantConnectionAsync(AdminScenario scenario, string id)
    {
        var row = await scenario.RowAsync(id);
        var password = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var connection = new NpgsqlConnection(scenario.TenantConnectionString(id, row.SchemaUser, password));
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private static async Task<List<(string Actor, JsonObject Details)>> AuditAsync(NpgsqlConnection connection, string action)
    {
        await using var command = new NpgsqlCommand("SELECT actor, details::text FROM axis.audit_records WHERE action = @action", connection);
        command.Parameters.AddWithValue("action", action);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var records = new List<(string, JsonObject)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), JsonNode.Parse(reader.GetString(1))!.AsObject()));
        }

        return records;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<string> RealmUserIdAsync(string realm, string email) =>
        (await GetJsonAsync($"admin/realms/{realm}/users?email={Uri.EscapeDataString(email)}&exact=true")).EnumerateArray().Single().GetProperty("id").GetString()!;

    // The tenant's axis client has no return addresses yet, so the realm refuses any sign-in until one is added.
    private async Task AllowReturnAddressAsync(string realm)
    {
        var axis = (await GetJsonAsync($"admin/realms/{realm}/clients?clientId=axis")).EnumerateArray().Single();
        var representation = JsonNode.Parse(axis.GetRawText())!.AsObject();
        representation["redirectUris"] = new JsonArray("http://localhost/*");
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var updated = await SendAsync(http, await MasterTokenAsync(http), HttpMethod.Put, $"admin/realms/{realm}/clients/{axis.GetProperty("id").GetString()}", representation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
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
