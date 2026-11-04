using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The provisioning API of a cell on its console host, with real Keycloak and PostgreSQL. The cell has three tenants and a
/// preview tenant in the platform tables: <c>a</c> is active and served at <c>localhost</c> from a migrated database, <c>b</c>
/// is active, and <c>c</c> is suspended. The operator realm is a realm of its own, made for the test, with the role
/// <c>cell-operator</c> and two service account clients, one with the role and one without. A tenant realm token is a token
/// of the development <c>axis</c> realm.
/// </summary>
public sealed class CellProvisioningApiTests(PostgreSqlFixture database, KeycloakFixture keycloak)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string OperatorRole = "cell-operator";
    private const string CallerClientId = "ops-caller";
    private const string NoRoleClientId = "ops-norole";
    private const string ClientSecret = "ops-development-secret";

    private static readonly Uri _console = new("http://console.test/");
    private static readonly Uri _tenantHost = new("http://localhost/");

    private readonly HttpClient _http = new();
    private readonly string _realm = $"ops-{Guid.NewGuid():N}"[..16];
    private Uri _baseAddress = null!;
    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "admin/realms"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        request.Content = JsonContent.Create(new
        {
            realm = _realm,
            enabled = true,
            roles = new { realm = new[] { new { name = OperatorRole } } },
            clients = new[] { CallerClientId, NoRoleClientId }.Select(clientId => new
            {
                clientId,
                enabled = true,
                publicClient = false,
                secret = ClientSecret,
                serviceAccountsEnabled = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = false,
            }),
            users = new[]
            {
                new
                {
                    username = $"service-account-{CallerClientId}",
                    enabled = true,
                    serviceAccountClientId = CallerClientId,
                    realmRoles = new[] { OperatorRole },
                },
                new
                {
                    username = $"service-account-{NoRoleClientId}",
                    enabled = true,
                    serviceAccountClientId = NoRoleClientId,
                    realmRoles = Array.Empty<string>(),
                },
            },
        });
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{_realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    [Fact]
    public async Task An_operator_client_with_the_role_reads_the_tenants_and_one_tenant_on_the_console_host()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var one = await GetAsync(client, "/api/cell/tenants/b", token);
        using var suspended = await GetAsync(client, "/api/cell/tenants/c", token);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(["a", "b", "c"], items.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.Equal(["active", "active", "suspended"], items.EnumerateArray().Select(item => item.GetProperty("status").GetString()).ToArray());
        Assert.All(items.EnumerateArray(), item => Assert.Equal(JsonValueKind.Null, item.GetProperty("application").ValueKind));
        Assert.Equal(["localhost"], Hosts(items[0]));
        Assert.Equal(["b.vn.example.test", "b2.vn.example.test"], Hosts(items[1]));
        Assert.Equal(["c.vn.example.test"], Hosts(items[2]));

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        var tenant = await one.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("b", tenant.GetProperty("id").GetString());
        Assert.Equal("active", tenant.GetProperty("status").GetString());
        Assert.Equal(["b.vn.example.test", "b2.vn.example.test"], Hosts(tenant));
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("suspended", (await suspended.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_list_leaves_out_a_preview_tenant_and_an_unknown_or_preview_id_answers_404()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var unknown = await GetAsync(client, "/api/cell/tenants/nobody", token);
        using var preview = await GetAsync(client, "/api/cell/tenants/a-preview", token);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_token_or_with_a_tenant_realm_token_answers_401()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var tenantRealmToken = await TokenAsync("axis", KeycloakFixture.PurchasingClientId, KeycloakFixture.PurchasingClientSecret);

        using var none = await GetAsync(client, "/api/cell/tenants", token: null);
        using var tenantRealm = await GetAsync(client, "/api/cell/tenants", tenantRealmToken);
        using var tenantRealmOne = await GetAsync(client, "/api/cell/tenants/a", tenantRealmToken);
        using var garbage = await GetAsync(client, "/api/cell/tenants", "not-a-token");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantRealm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantRealmOne.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
    }

    [Fact]
    public async Task An_operator_client_without_the_role_answers_403()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var one = await GetAsync(client, "/api/cell/tenants/a", token);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, one.StatusCode);
    }

    [Fact]
    public async Task The_console_host_serves_only_the_probes_and_the_cell_routes()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var live = await GetAsync(client, "/health/live", token: null);
        using var page = await GetAsync(client, "/", token: null);
        using var me = await GetAsync(client, "/api/me", token: null);
        using var meWithToken = await GetAsync(client, "/api/me", token);
        using var other = await GetAsync(client, "/api/cell/other", token);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, me.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, meWithToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task A_tenant_host_answers_404_for_the_cell_routes_before_any_sign_in_work()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _tenantHost);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var none = await GetAsync(client, "/api/cell/tenants", token: null);
        using var operatorToken = await GetAsync(client, "/api/cell/tenants", token);
        using var one = await GetAsync(client, "/api/cell/tenants/a", token);
        using var me = await GetAsync(client, "/api/me", token: null);

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, operatorToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, one.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task An_operator_sets_and_clears_quotas_and_the_tenant_read_and_an_audit_record_show_them()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var set = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["users"] = 100, ["storage"] = null } });
        using var afterSet = await GetAsync(client, "/api/cell/tenants/b", token);
        using var remove = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { remove = new List<string> { "users" } });
        using var afterRemove = await GetAsync(client, "/api/cell/tenants/b", token);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var setQuotas = (await set.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas");
        AssertQuota(setQuotas, "users", 100, true);
        AssertQuota(setQuotas, "storage", null, true);
        AssertQuota(setQuotas, "applications", 10, false);
        var readQuotas = (await afterSet.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas");
        AssertQuota(readQuotas, "users", 100, true);
        AssertQuota(readQuotas, "storage", null, true);
        AssertQuota(readQuotas, "applications", 10, false);
        AssertQuota(readQuotas, "authoringRequests", null, false);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        AssertQuota((await remove.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas"), "users", 50, false);
        var removedQuotas = (await afterRemove.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas");
        AssertQuota(removedQuotas, "users", 50, false);
        AssertQuota(removedQuotas, "storage", null, true);

        var records = await AuditRecordsAsync();
        Assert.Equal(
            [("tenant quotas set", "b", "storage,users"), ("tenant quotas set", "b", "users")],
            records);
    }

    [Fact]
    public async Task An_unknown_key_or_a_negative_value_answers_400_and_writes_nothing()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var unknown = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["nope"] = 1 } });
        using var negative = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["users"] = -1 } });
        using var unknownRemoved = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { remove = new List<string> { "nope" } });
        using var both = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["users"] = 1 }, remove = new List<string> { "users" } });
        using var empty = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { });

        foreach (var response in new[] { unknown, negative, unknownRemoved, both, empty })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(0, await CountAsync("platform.tenant_quotas"));
        Assert.Equal(0, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task Setting_quotas_without_the_role_answers_403_and_an_unknown_or_preview_tenant_answers_404()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);
        var body = new { set = new Dictionary<string, long?> { ["users"] = 1 } };

        using var noRole = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", noRoleToken, body);
        using var none = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token: null, body);
        using var unknown = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/nobody/quotas", operatorToken, body);
        using var preview = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/a-preview/quotas", operatorToken, body);

        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(0, await CountAsync("platform.tenant_quotas"));
        Assert.Equal(0, await CountAsync("platform.audit_records"));
    }

    private static void AssertQuota(JsonElement quotas, string key, long? value, bool overridden)
    {
        var quota = quotas.GetProperty(key);
        Assert.Equal(value, quota.GetProperty("value").ValueKind == JsonValueKind.Null ? null : quota.GetProperty("value").GetInt64());
        Assert.Equal(overridden, quota.GetProperty("overridden").GetBoolean());
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<List<(string Command, string Tenant, string Changed)>> AuditRecordsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, array_to_string(changed, ',') FROM platform.audit_records ORDER BY occurred_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var records = new List<(string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, object body)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            // A write without a bearer token is a session write, which needs the CSRF header, so the answer comes from authentication.
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static string[] Hosts(JsonElement tenant) =>
        [.. tenant.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()!)];

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, Uri address) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = address, AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<string> TokenAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await _http.PostAsync(
            new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<WebApplicationFactory<Program>> StartAsync()
    {
        var authority = await keycloak.StartAsync();
        var platform = await CellTestPlatform.CreateAsync(database, authority, CancellationToken);
        _platform = platform;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:ConsoleHost", _console.Host);
            builder.UseSetting("Cell:OperatorRealm", _realm);
            builder.UseSetting("Cell:OperatorRole", OperatorRole);
            builder.UseSetting("IdentityProvider:Url", _baseAddress.ToString());
        });
    }
}
