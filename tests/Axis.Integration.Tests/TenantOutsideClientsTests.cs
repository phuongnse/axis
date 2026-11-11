using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Admin;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant administrator's outside MCP client switch and connected apps on the tenant's own host, see
/// <c>docs/reference/user-administration.md#outside-mcp-clients</c>, with real PostgreSQL and Keycloak. Each test makes the realm
/// of a tenant with a unique name, with two public clients, <c>listed</c> and <c>other</c>, and deletes it at the end. A person's
/// consents are made through Keycloak's partial import, because the route only reads what Keycloak stores.
/// </summary>
public sealed class TenantOutsideClientsTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string AdministratorRole = "tenant-administrator";
    private const string ActivationSecret = "activation-secret-1";
    private const string Listed = "listed";
    private const string Other = "other";
    private const string ConnectedApps = "/api/tenant/connected-apps";

    private static readonly string[] _grantedScopes = ["profile"];

    private readonly HttpClient _http = new();
    private Uri _baseAddress = null!;
    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task A_member_is_refused_with_403_and_a_caller_with_no_session_with_401_and_nothing_is_written()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var member = Client(factory, id).SignedInAs("member");
            using var anonymous = Client(factory, id);

            using var memberRead = await SendAsync(member, HttpMethod.Get);
            using var memberWrite = await SendAsync(member, HttpMethod.Put, new { outsideClients = "all" });
            using var anonymousRead = await SendAsync(anonymous, HttpMethod.Get);
            using var anonymousWrite = await SendAsync(anonymous, HttpMethod.Put, new { outsideClients = "all" });

            Assert.Equal(HttpStatusCode.Forbidden, memberRead.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, memberWrite.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
            Assert.Equal(0, await CountAsync("platform.audit_records"));
            Assert.Equal(("none", ""), await StoredAsync(id));
            Assert.True(await EnabledAsync(id, Listed));
            Assert.True(await EnabledAsync(id, Other));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task An_administrators_PUT_with_allowList_stores_it_turns_the_listed_client_on_and_writes_one_record()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var set = await SendAsync(client, HttpMethod.Put, new { outsideClients = "allowList", allowedClients = new[] { $" {Listed} ", Listed } });
            using var read = await SendAsync(client, HttpMethod.Get);

            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
            Assert.Equal(("allowList", Listed), await StoredAsync(id));
            Assert.True(await EnabledAsync(id, Listed));
            Assert.False(await EnabledAsync(id, Other));
            Assert.Equal([("tenant outside-clients set", id, "admin", "outsideClients,allowedClients")], await RecordsAsync());
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var body = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal("allowList", body.GetProperty("outsideClients").GetString());
            Assert.Equal([Listed], body.GetProperty("allowedClients").EnumerateArray().Select(item => item.GetString()).ToArray());
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task An_unknown_mode_a_missing_body_and_a_blank_client_id_answer_400_and_change_nothing()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            var statuses = new List<HttpStatusCode>();
            foreach (var body in new object?[]
            {
                new { outsideClients = "ALL" },
                new { outsideClients = "some" },
                new { allowedClients = new[] { Listed } },
                null,
                new { outsideClients = "allowList", allowedClients = new[] { "  " } },
            })
            {
                using var response = await SendAsync(client, HttpMethod.Put, body);
                statuses.Add(response.StatusCode);
            }

            Assert.All(statuses, status => Assert.Equal(HttpStatusCode.BadRequest, status));
            Assert.Equal(5, statuses.Count);
            Assert.Equal(("none", ""), await StoredAsync(id));
            Assert.Equal(0, await CountAsync("platform.audit_records"));
            Assert.True(await EnabledAsync(id, Listed));
            Assert.True(await EnabledAsync(id, Other));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_realm_that_refuses_the_activation_secret_answers_502_and_the_stored_switch_and_its_record_remain()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, "wrong-activation-secret", realmActivationSecret: ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var set = await SendAsync(client, HttpMethod.Put, new { outsideClients = "none" });
            using var again = await SendAsync(client, HttpMethod.Put, new { outsideClients = "allowList", allowedClients = new[] { Listed } });

            Assert.Equal(HttpStatusCode.BadGateway, set.StatusCode);
            Assert.Equal(HttpStatusCode.BadGateway, again.StatusCode);
            Assert.Equal(("allowList", Listed), await StoredAsync(id));
            Assert.Equal(2, await CountAsync("platform.audit_records WHERE command = 'tenant outside-clients set'"));
            Assert.True(await EnabledAsync(id, Listed));
            Assert.True(await EnabledAsync(id, Other));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_person_who_consented_to_a_public_client_is_listed_with_the_consent_time_and_the_last_use_and_other_clients_are_not()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            var userId = await AddPersonWithConsentsAsync(id, Listed, "account-console", "axis");
            var consentedAt = await ConsentTimeAsync(id, userId, Listed);
            await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase,
                """INSERT INTO axis.mcp_clients (client_id, last_used_at) VALUES ('listed', '2026-03-04T05:06:07Z') RETURNING client_id""",
                CancellationToken);
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var response = await SendAsync(client, HttpMethod.Get, path: ConnectedApps);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            var item = Assert.Single(body.GetProperty("items").EnumerateArray());
            Assert.Equal(Listed, item.GetProperty("clientId").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("name").ValueKind);
            Assert.Equal(DateTimeOffset.Parse("2026-03-04T05:06:07Z"), item.GetProperty("lastUsedAt").GetDateTimeOffset());
            var person = Assert.Single(item.GetProperty("people").EnumerateArray());
            Assert.Equal(userId, person.GetProperty("userId").GetString());
            Assert.Equal("Pat Example", person.GetProperty("displayName").GetString());
            Assert.Equal("pat@example.test", person.GetProperty("email").GetString());
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(consentedAt), person.GetProperty("consentedAt").GetDateTimeOffset());
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_client_that_was_never_used_has_a_null_last_use_and_a_realm_with_no_consent_answers_an_empty_list()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var none = await SendAsync(client, HttpMethod.Get, path: ConnectedApps);
            await AddPersonWithConsentsAsync(id, Other);
            using var unused = await SendAsync(client, HttpMethod.Get, path: ConnectedApps);

            Assert.Equal(HttpStatusCode.OK, none.StatusCode);
            Assert.Empty((await none.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, unused.StatusCode);
            var item = Assert.Single((await unused.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items").EnumerateArray());
            Assert.Equal(Other, item.GetProperty("clientId").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("lastUsedAt").ValueKind);
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task Consents_to_Keycloaks_own_and_confidential_clients_are_never_listed()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await AddPersonWithConsentsAsync(id, "account-console", "axis");
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var response = await SendAsync(client, HttpMethod.Get, path: ConnectedApps);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items").EnumerateArray());
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task The_connected_apps_route_refuses_a_member_with_403_and_a_caller_with_no_session_with_401()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await AddPersonWithConsentsAsync(id, Listed);
            await using var factory = Start();
            using var member = Client(factory, id).SignedInAs("member");
            using var anonymous = Client(factory, id);

            using var memberRead = await SendAsync(member, HttpMethod.Get, path: ConnectedApps);
            using var anonymousRead = await SendAsync(anonymous, HttpMethod.Get, path: ConnectedApps);

            Assert.Equal(HttpStatusCode.Forbidden, memberRead.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_member_a_caller_with_no_session_axis_agent_and_an_unknown_client_are_refused_and_nothing_is_written()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            var userId = await AddPersonWithConsentsAsync(id, Listed);
            await using var factory = Start();
            using var admin = Client(factory, id).SignedInAs("admin");
            using var member = Client(factory, id).SignedInAs("member");
            using var anonymous = Client(factory, id);
            var path = $"{ConnectedApps}/{Listed}/revoke";

            using var anonymousRevoke = await SendAsync(anonymous, HttpMethod.Post, path: path);
            using var memberRevoke = await SendAsync(member, HttpMethod.Post, path: path);
            using var agentRevoke = await SendAsync(admin, HttpMethod.Post, path: $"{ConnectedApps}/axis-agent/revoke");
            using var unknownRevoke = await SendAsync(admin, HttpMethod.Post, path: $"{ConnectedApps}/no-such-client/revoke");

            Assert.Equal(HttpStatusCode.Unauthorized, anonymousRevoke.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, memberRevoke.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, agentRevoke.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknownRevoke.StatusCode);
            Assert.Equal(0, await CountAsync("platform.audit_records"));
            Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(tenantDatabase, "SELECT count(*) FROM axis.audit_records", CancellationToken));
            Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(tenantDatabase, "SELECT count(*) FROM axis.mcp_clients", CancellationToken));
            Assert.True(await ConsentTimeAsync(id, userId, Listed) > 0);
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_revoke_of_a_client_nobody_consented_to_still_stores_the_revoke_and_writes_a_record_with_no_people()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            var userId = await AddPersonWithConsentsAsync(id, Other);
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var response = await SendAsync(client, HttpMethod.Post, path: $"{ConnectedApps}/{Listed}/revoke");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(1L, await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase, "SELECT count(*) FROM axis.mcp_clients WHERE client_id = 'listed' AND revoked_at IS NOT NULL", CancellationToken));
            Assert.Equal(1L, await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase,
                """SELECT count(*) FROM axis.audit_records WHERE action = 'mcp.consentRevoked' AND details = '{"clientId": "listed", "userIds": []}'::jsonb""",
                CancellationToken));
            Assert.Equal([("tenant connected-app revoke", id, "admin", "consent")], await RecordsAsync());
            Assert.True(await ConsentTimeAsync(id, userId, Other) > 0);
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_realm_that_refuses_the_activation_secret_answers_502_to_the_connected_apps_route_with_a_fixed_title()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, "wrong-activation-secret", realmActivationSecret: ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var response = await SendAsync(client, HttpMethod.Get, path: ConnectedApps);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal("The identity provider could not list the connected apps.", body.GetProperty("title").GetString());
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    // A null body sends no body.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, object? body = null, string path = "/api/tenant/outside-clients")
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string id) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{id}.vn.example.test"), AllowAutoRedirect = false });

    private WebApplicationFactory<Program> Start()
    {
        var platform = _platform;
        var authority = new Uri(_baseAddress, "realms/axis");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:ConsoleHost", "console.test");
            builder.UseSetting("Cell:OperatorRealm", "operator");
            builder.UseSetting("Cell:OperatorRole", "cell-operator");
            builder.UseSetting("IdentityProvider:Url", authority.ToString());
            builder.UseSetting("Users:AdministratorRole", AdministratorRole);
            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole), new TestIdentity("member", "Member"));
        });
    }

    // Creates the platform database. The tenants are added before the server starts, so it finds them.
    private async Task<string> PrepareAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _platform = await CellTestPlatform.CreateAsync(database, new Uri(_baseAddress, "realms/axis"), CancellationToken);
        return _platform;
    }

    /// <summary>
    /// Makes a tenant and its realm, with the public clients <c>listed</c> and <c>other</c>. The tenant stores
    /// <paramref name="storedActivationSecret"/>, and the realm's <c>axis-activation</c> client has
    /// <paramref name="realmActivationSecret"/>, which is the same unless a test sets it apart. It returns the tenant's id and the connection string of its database.
    /// </summary>
    private async Task<(string Id, string Database)> AddRealmTenantAsync(string platform, string storedActivationSecret, string? realmActivationSecret = null)
    {
        var id = $"oc-{Guid.NewGuid():N}"[..16];
        const string usersSecret = "users-secret-1";
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(id, "Acme", new TenantRealmSecrets("platform-secret-1", realmActivationSecret ?? storedActivationSecret, usersSecret, "agent-secret-1"), new Dictionary<string, string>(), CancellationToken);
        string tenantDatabase;
        try
        {
            tenantDatabase = await CellTestPlatform.AddRealmTenantAsync(
                database, platform, id, "active", _baseAddress, usersSecret, CancellationToken, storedActivationSecret, $"{id}.vn.example.test");
            var admin = await MasterTokenAsync();
            foreach (var clientId in new[] { Listed, Other })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, $"admin/realms/{id}/clients"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
                request.Content = JsonContent.Create(new { clientId, enabled = true, publicClient = true, standardFlowEnabled = true });
                using var response = await _http.SendAsync(request, CancellationToken);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
        }
        catch
        {
            await DeleteRealmAsync(id);
            throw;
        }

        return (id, tenantDatabase);
    }

    // Creates the person Pat with one consent to each of the clients, and returns the person's id.
    private async Task<string> AddPersonWithConsentsAsync(string realm, params string[] clientIds)
    {
        var admin = await MasterTokenAsync();
        using var import = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, $"admin/realms/{realm}/partialImport"));
        import.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        import.Content = JsonContent.Create(new
        {
            ifResourceExists = "FAIL",
            users = new[]
            {
                new
                {
                    username = "pat",
                    email = "pat@example.test",
                    firstName = "Pat",
                    lastName = "Example",
                    enabled = true,
                    clientConsents = clientIds.Select(clientId => new { clientId, grantedClientScopes = _grantedScopes }).ToArray(),
                },
            },
        });
        using var imported = await _http.SendAsync(import, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        using var find = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, $"admin/realms/{realm}/users?username=pat&exact=true"));
        find.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var found = await _http.SendAsync(find, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var users = await found.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return users.EnumerateArray().Single().GetProperty("id").GetString()!;
    }

    // What Keycloak stores as the consent time of a person to a client, in milliseconds since 1970.
    private async Task<long> ConsentTimeAsync(string realm, string userId, string clientId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, $"admin/realms/{realm}/users/{userId}/consents"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var consents = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return consents.EnumerateArray().Single(consent => consent.GetProperty("clientId").GetString() == clientId).GetProperty("createdDate").GetInt64();
    }

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

    private async Task<bool> EnabledAsync(string realm, string clientId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, $"admin/realms/{realm}/clients?clientId={clientId}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clients = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return clients.EnumerateArray().Single().GetProperty("enabled").GetBoolean();
    }

    private async Task DeleteRealmAsync(string realm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    private async Task<(string Mode, string Allowed)> StoredAsync(string id)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT ai_outside_clients, array_to_string(ai_allowed_clients, ',') FROM platform.tenants WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<List<(string Command, string TenantId, string Actor, string Changed)>> RecordsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, actor, array_to_string(changed, ',') FROM platform.audit_records ORDER BY occurred_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var records = new List<(string, string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return records;
    }

    private async Task<long> CountAsync(string from)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {from}", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
