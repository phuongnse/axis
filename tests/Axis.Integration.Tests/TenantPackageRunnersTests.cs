using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// A tenant administrator's package runners on the tenant's own host, see
/// <c>docs/reference/user-administration.md#package-runners</c>, with real PostgreSQL and Keycloak. Each test makes the realm of a
/// tenant with a unique name and deletes it at the end.
/// </summary>
public sealed class TenantPackageRunnersTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string AdministratorRole = "tenant-administrator";
    private const string ActivationSecret = "activation-secret-1";
    private const string ChangedActivationSecret = "activation-secret-2";
    private const string Runners = "/api/tenant/package-runners";

    private readonly HttpClient _http = new();
    private Uri _baseAddress = null!;
    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task The_checks_run_in_order_and_a_refused_request_stores_no_runner_writes_no_record_and_makes_no_client()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        var previewId = $"pv-{Guid.NewGuid():N}"[..16];
        await CellTestPlatform.AddRealmTenantAsync(
            database, tenant, previewId, "active", _baseAddress, "users-secret-1", CancellationToken, ActivationSecret, $"{previewId}.vn.example.test", previewOf: id);
        try
        {
            await using var factory = Start();
            using var anonymous = Client(factory, id);
            using var member = Client(factory, id).SignedInAs("member");
            using var admin = Client(factory, id).SignedInAs("admin");
            using var stale = Client(factory, id).SignedInAs("stale-admin");
            using var preview = Client(factory, previewId).SignedInAs("admin");

            var statuses = new List<(string Case, HttpStatusCode Status)>();
            async Task Check(string name, HttpClient client, HttpMethod method, object? body, string path = Runners)
            {
                using var response = await SendAsync(client, method, body, path);
                statuses.Add((name, response.StatusCode));
            }

            await Check("anonymous create", anonymous, HttpMethod.Post, new { name = "Plant" });
            await Check("anonymous list", anonymous, HttpMethod.Get, null);
            await Check("member create", member, HttpMethod.Post, new { name = "Plant" });
            await Check("member list", member, HttpMethod.Get, null);
            await Check("member revoke", member, HttpMethod.Post, null, $"{Runners}/{Guid.NewGuid()}/revoke");
            await Check("preview create", preview, HttpMethod.Post, new { name = "Plant" });
            await Check("preview list", preview, HttpMethod.Get, null);
            await Check("blank name", admin, HttpMethod.Post, new { name = "   " });
            await Check("long name", admin, HttpMethod.Post, new { name = new string('x', 101) });
            await Check("no body", admin, HttpMethod.Post, null);
            await Check("stale with blank name", stale, HttpMethod.Post, new { name = " " });
            await Check("stale with a name", stale, HttpMethod.Post, new { name = "Plant" });

            Assert.Equal(
                [
                    ("anonymous create", HttpStatusCode.Unauthorized),
                    ("anonymous list", HttpStatusCode.Unauthorized),
                    ("member create", HttpStatusCode.Forbidden),
                    ("member list", HttpStatusCode.Forbidden),
                    ("member revoke", HttpStatusCode.Forbidden),
                    ("preview create", HttpStatusCode.NotFound),
                    ("preview list", HttpStatusCode.NotFound),
                    ("blank name", HttpStatusCode.BadRequest),
                    ("long name", HttpStatusCode.BadRequest),
                    ("no body", HttpStatusCode.BadRequest),
                    ("stale with blank name", HttpStatusCode.BadRequest),
                    ("stale with a name", HttpStatusCode.Forbidden),
                ],
                statuses);
            Assert.Equal(0, await CountAsync(tenantDatabase, "axis.package_runners"));
            Assert.Equal(0, await CountAsync(tenantDatabase, "axis.audit_records"));
            Assert.Empty(await RunnerClientsAsync(id));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_create_answers_201_with_a_secret_that_gets_a_runner_token_and_the_record_holds_no_secret()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var created = await SendAsync(client, HttpMethod.Post, new { name = "Plant" });

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            var runnerId = body.GetProperty("id").GetGuid();
            var clientId = body.GetProperty("clientId").GetString()!;
            var secret = body.GetProperty("clientSecret").GetString()!;
            Assert.Equal($"runner-{runnerId:N}", clientId);
            Assert.Equal(64, secret.Length);
            Assert.Equal($"{Runners}/{runnerId}", created.Headers.Location!.OriginalString);

            var audiences = await TokenAudiencesAsync(id, clientId, secret);
            Assert.Contains("axis-package-runner", audiences);

            var details = (string)(await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase,
                "SELECT details::text FROM axis.audit_records WHERE action = 'packageRunner.created'",
                CancellationToken))!;
            Assert.DoesNotContain(secret, details, StringComparison.Ordinal);
            using var record = JsonDocument.Parse(details);
            Assert.Equal(runnerId.ToString(), record.RootElement.GetProperty("id").GetString());
            Assert.Equal(clientId, record.RootElement.GetProperty("clientId").GetString());
            Assert.Equal(1, await CountAsync(tenantDatabase, "axis.audit_records"));
            Assert.Equal("admin", await PostgreSqlServer.AdminScalarAsync(tenantDatabase, "SELECT actor FROM axis.audit_records", CancellationToken));
            Assert.Equal(
                0,
                await CountAsync(tenantDatabase, $"axis.package_runners WHERE name || client_id || packages::text LIKE '%{secret}%'"));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task The_list_answers_the_runner_with_its_name_and_client_id_and_no_secret_and_a_revoked_runner_is_not_listed()
    {
        var tenant = await PrepareAsync();
        var (id, _) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");
            using var created = await SendAsync(client, HttpMethod.Post, new { name = "  Plant  " });
            var runnerId = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();

            using var listed = await SendAsync(client, HttpMethod.Get);

            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var text = await listed.Content.ReadAsStringAsync(CancellationToken);
            Assert.DoesNotContain("ecret", text, StringComparison.Ordinal);
            var item = Assert.Single(JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(runnerId, item.GetProperty("id").GetGuid());
            Assert.Equal("Plant", item.GetProperty("name").GetString());
            Assert.Equal($"runner-{runnerId:N}", item.GetProperty("clientId").GetString());
            Assert.Empty(item.GetProperty("packages").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("lastCallAt").ValueKind);
            Assert.True(item.GetProperty("createdAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));

            using var revoked = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/{runnerId}/revoke");
            using var after = await SendAsync(client, HttpMethod.Get);

            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            var items = (await after.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
            Assert.Empty(items.EnumerateArray());
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_revoke_answers_204_stores_the_revoke_writes_one_record_and_deletes_the_client_and_an_unknown_id_answers_404()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");
            using var created = await SendAsync(client, HttpMethod.Post, new { name = "Plant" });
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            var runnerId = body.GetProperty("id").GetGuid();
            var clientId = body.GetProperty("clientId").GetString()!;
            Assert.Equal([clientId], await RunnerClientsAsync(id));

            using var revoked = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/{runnerId}/revoke");
            using var unknown = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/{Guid.NewGuid()}/revoke");
            using var notAnId = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/not-an-id/revoke");

            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
            Assert.NotNull(await RevokedAtAsync(tenantDatabase, runnerId));
            Assert.Empty(await RunnerClientsAsync(id));
            var details = (string)(await PostgreSqlServer.AdminScalarAsync(
                tenantDatabase,
                "SELECT details::text FROM axis.audit_records WHERE action = 'packageRunner.revoked'",
                CancellationToken))!;
            using var record = JsonDocument.Parse(details);
            Assert.Equal(runnerId.ToString(), record.RootElement.GetProperty("id").GetString());
            Assert.Equal(clientId, record.RootElement.GetProperty("clientId").GetString());
            Assert.Equal(1, await CountAsync(tenantDatabase, "axis.audit_records WHERE action = 'packageRunner.revoked'"));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_realm_that_refuses_the_activation_secret_answers_502_to_a_create_and_stores_nothing()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, "wrong-activation-secret", realmActivationSecret: ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");

            using var response = await SendAsync(client, HttpMethod.Post, new { name = "Plant" });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.DoesNotContain("clientSecret", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
            Assert.Equal(0, await CountAsync(tenantDatabase, "axis.package_runners"));
            Assert.Equal(0, await CountAsync(tenantDatabase, "axis.audit_records"));
            Assert.Empty(await RunnerClientsAsync(id));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_revoke_while_the_realm_fails_answers_502_and_keeps_the_stored_revoke_and_a_repeat_deletes_the_client()
    {
        var tenant = await PrepareAsync();
        var (id, tenantDatabase) = await AddRealmTenantAsync(tenant, ActivationSecret);
        try
        {
            await using var factory = Start();
            using var client = Client(factory, id).SignedInAs("admin");
            using var created = await SendAsync(client, HttpMethod.Post, new { name = "Plant" });
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            var runnerId = body.GetProperty("id").GetGuid();
            var clientId = body.GetProperty("clientId").GetString()!;

            // The realm no longer accepts the secret the tenant stores.
            await SetActivationSecretAsync(id, ChangedActivationSecret);
            using var failed = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/{runnerId}/revoke");

            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            var revokedAt = await RevokedAtAsync(tenantDatabase, runnerId);
            Assert.NotNull(revokedAt);
            Assert.Equal([clientId], await RunnerClientsAsync(id));
            Assert.Equal(1, await CountAsync(tenantDatabase, "axis.audit_records WHERE action = 'packageRunner.revoked'"));

            await SetActivationSecretAsync(id, ActivationSecret);
            using var repeated = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/{runnerId}/revoke");
            using var again = await SendAsync(client, HttpMethod.Post, null, $"{Runners}/{runnerId}/revoke");

            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
            Assert.Empty(await RunnerClientsAsync(id));
            Assert.Equal(revokedAt, await RevokedAtAsync(tenantDatabase, runnerId));
            Assert.Equal(1, await CountAsync(tenantDatabase, "axis.audit_records WHERE action = 'packageRunner.revoked'"));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    // A null body sends no body.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, object? body = null, string path = Runners)
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
            builder.UseTestAuthentication(
                new TestIdentity("admin", "Admin", AdministratorRole) { SignedInAgo = TimeSpan.Zero },
                new TestIdentity("stale-admin", "Stale Admin", AdministratorRole) { SignedInAgo = TimeSpan.FromMinutes(6) },
                new TestIdentity("member", "Member") { SignedInAgo = TimeSpan.Zero });
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
    /// Makes a tenant and its realm. The tenant stores <paramref name="storedActivationSecret"/>, and the realm's <c>axis-activation</c>
    /// client has <paramref name="realmActivationSecret"/>, which is the same unless a test sets it apart. It returns the tenant's id and
    /// the connection string of its database.
    /// </summary>
    private async Task<(string Id, string Database)> AddRealmTenantAsync(string platform, string storedActivationSecret, string? realmActivationSecret = null)
    {
        var id = $"pr-{Guid.NewGuid():N}"[..16];
        const string usersSecret = "users-secret-1";
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(id, "Acme", new TenantRealmSecrets("platform-secret-1", realmActivationSecret ?? storedActivationSecret, usersSecret, "agent-secret-1"), new Dictionary<string, string>(), CancellationToken);
        try
        {
            var tenantDatabase = await CellTestPlatform.AddRealmTenantAsync(
                database, platform, id, "active", _baseAddress, usersSecret, CancellationToken, storedActivationSecret, $"{id}.vn.example.test");
            return (id, tenantDatabase);
        }
        catch
        {
            await DeleteRealmAsync(id);
            throw;
        }
    }

    // Signs in as the runner's client and returns the audience of its access token.
    private async Task<string[]> TokenAudiencesAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("access_token").GetString()!;
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
        var audience = claims.GetProperty("aud");
        return audience.ValueKind == JsonValueKind.Array
            ? [.. audience.EnumerateArray().Select(item => item.GetString()!)]
            : [audience.GetString()!];
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

    // The client ids of the realm's package runner clients.
    private async Task<List<string>> RunnerClientsAsync(string realm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, $"admin/realms/{realm}/clients?search=true&clientId=runner-"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clients = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return [.. clients.EnumerateArray().Select(client => client.GetProperty("clientId").GetString()!).Where(clientId => clientId.StartsWith("runner-", StringComparison.Ordinal))];
    }

    // Changes the secret of the realm's axis-activation client, through the master realm's administration.
    private async Task SetActivationSecretAsync(string realm, string secret)
    {
        var admin = await MasterTokenAsync();
        using var find = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, $"admin/realms/{realm}/clients?clientId=axis-activation"));
        find.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var found = await _http.SendAsync(find, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var activation = (await found.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).EnumerateArray().Single();
        var representation = activation.Deserialize<Dictionary<string, JsonElement>>()!;
        representation["secret"] = JsonSerializer.SerializeToElement(secret);
        using var update = new HttpRequestMessage(HttpMethod.Put, new Uri(_baseAddress, $"admin/realms/{realm}/clients/{activation.GetProperty("id").GetString()}"));
        update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        update.Content = JsonContent.Create(representation);
        using var updated = await _http.SendAsync(update, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    private async Task DeleteRealmAsync(string realm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    private static async Task<long> CountAsync(string tenantDatabase, string from) =>
        Convert.ToInt64(await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"SELECT count(*) FROM {from}", CancellationToken));

    private static async Task<string?> RevokedAtAsync(string tenantDatabase, Guid id) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(
            tenantDatabase, $"SELECT revoked_at::text FROM axis.package_runners WHERE id = '{id}'", CancellationToken);
}
