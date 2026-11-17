using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// A customer-hosted runner signs in with its client's token and registers its packages at <c>POST /api/package-runner/packages</c>, see
/// <c>docs/reference/extension-packages.md#routes</c>, with real PostgreSQL and Keycloak. Each test makes the realms of its tenants with
/// unique names and deletes them at the end.
/// </summary>
public sealed class PackageRunnerRegistrationTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>, IDisposable
{
    private const string AdministratorRole = "tenant-administrator";
    private const string ActivationSecret = "activation-secret-1";
    private const string Runners = "/api/tenant/package-runners";
    private const string Registration = "/api/package-runner/packages";
    private const string ServiceAudience = "axis-services";

    private static readonly string _sumManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "sum");
    private static readonly string _multiplyManifest = CustomerHostedReleases.ToolsManifest("1.0.0", "multiply");

    private readonly HttpClient _http = new();
    private readonly TestClock _clock = new();
    private Uri _baseAddress = null!;
    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task The_checks_run_in_order_unknown_host_then_the_token_and_the_runner_row_then_the_body()
    {
        await PrepareAsync();
        var (id, tenantDatabase) = await AddTenantAsync();
        var (otherId, _) = await AddTenantAsync();
        try
        {
            await using var factory = Start();
            using var admin = Client(factory, id).SignedInAs("admin");
            var good = await CreateRunnerAsync(admin, id);
            var resigned = await CreateRunnerAsync(admin, id);
            using var otherAdmin = Client(factory, otherId).SignedInAs("admin");
            var otherRealm = await CreateRunnerAsync(otherAdmin, otherId);
            var deleted = await CreateRunnerAsync(admin, id);
            var revoked = await CreateRunnerAsync(admin, id);
            var expired = await CreateRunnerAsync(admin, id);
            await CreateClientAsync(id, "wrong-audience-client", "wrong-audience-secret", ServiceAudience);
            var wrongAudience = await TokenAsync(id, "wrong-audience-client", "wrong-audience-secret");

            // A token from the realm, so only the check under test refuses it. The row of the runner is removed or revoked after the token is made.
            await PostgreSqlServer.AdminScalarAsync(tenantDatabase, $"DELETE FROM axis.package_runners WHERE id = '{deleted.Id}'", CancellationToken);
            using (var revoke = await SendAsync(admin, HttpMethod.Post, $"{Runners}/{revoked.Id}/revoke", csrf: true))
            {
                Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            }

            var valid = Body(_sumManifest);
            var statuses = new List<(string Case, HttpStatusCode Status)>();
            async Task Check(string name, string host, string? token, string? body, string contentType = "application/json")
            {
                using var client = Client(factory, host);
                using var response = await RegisterAsync(client, token, body, contentType);
                statuses.Add((name, response.StatusCode));
            }

            await Check("unknown host", "unknown", good.Token, valid);
            await Check("no token", id, null, valid);
            await Check("bad signature", id, EndpointCallerTests.Resign(resigned.Token), valid);
            await Check("another realm", id, otherRealm.Token, valid);
            await Check("wrong audience", id, wrongAudience, valid);
            await Check("deleted row", id, deleted.Token, valid);
            await Check("revoked runner", id, revoked.Token, valid);
            await Check("revoked runner with a bad body", id, revoked.Token, "{}");

            _clock.Advance(TimeSpan.FromHours(1));
            await Check("expired token", id, expired.Token, valid);
            _clock.Advance(TimeSpan.FromHours(-1));

            await Check("no body", id, good.Token, null);
            await Check("not JSON", id, good.Token, "not json");
            await Check("not a JSON content type", id, good.Token, valid, "text/plain");
            await Check("no packages property", id, good.Token, "{}");
            await Check("empty packages", id, good.Token, """{ "packages": [] }""");
            await Check("packages is not a list", id, good.Token, """{ "packages": {} }""");
            await Check("no manifest", id, good.Token, """{ "packages": [{}] }""");
            await Check("no version", id, good.Token, """{ "packages": [{ "manifest": { "id": "tools" } }] }""");
            await Check("blank id", id, good.Token, """{ "packages": [{ "manifest": { "id": "", "version": "1.0.0" } }] }""");

            Assert.Equal(
                [
                    ("unknown host", HttpStatusCode.NotFound),
                    ("no token", HttpStatusCode.Unauthorized),
                    ("bad signature", HttpStatusCode.Unauthorized),
                    ("another realm", HttpStatusCode.Unauthorized),
                    ("wrong audience", HttpStatusCode.Unauthorized),
                    ("deleted row", HttpStatusCode.Unauthorized),
                    ("revoked runner", HttpStatusCode.Unauthorized),
                    ("revoked runner with a bad body", HttpStatusCode.Unauthorized),
                    ("expired token", HttpStatusCode.Unauthorized),
                    ("no body", HttpStatusCode.BadRequest),
                    ("not JSON", HttpStatusCode.BadRequest),
                    ("not a JSON content type", HttpStatusCode.BadRequest),
                    ("no packages property", HttpStatusCode.BadRequest),
                    ("empty packages", HttpStatusCode.BadRequest),
                    ("packages is not a list", HttpStatusCode.BadRequest),
                    ("no manifest", HttpStatusCode.BadRequest),
                    ("no version", HttpStatusCode.BadRequest),
                    ("blank id", HttpStatusCode.BadRequest),
                ],
                statuses);
        }
        finally
        {
            await DeleteRealmAsync(id);
            await DeleteRealmAsync(otherId);
        }
    }

    [Fact]
    public async Task A_service_account_token_is_a_401_on_the_runner_route_and_a_runner_token_is_a_401_on_a_record_route()
    {
        await PrepareAsync();
        var (id, _) = await AddTenantAsync(ServiceAudience);
        try
        {
            await using var factory = Start();
            using var admin = Client(factory, id).SignedInAs("admin");
            var runner = await CreateRunnerAsync(admin, id);
            await CreateClientAsync(id, "service-client", "service-secret", ServiceAudience);
            var service = await TokenAsync(id, "service-client", "service-secret");
            using var client = Client(factory, id);
            const string RecordRoute = "/api/apps/purchasing/entities/request/records";

            using var serviceOnRunnerRoute = await RegisterAsync(client, service, Body(_sumManifest));
            using var runnerOnRecordRoute = await GetAsync(client, RecordRoute, runner.Token);
            using var serviceOnRecordRoute = await GetAsync(client, RecordRoute, service);

            Assert.Equal(HttpStatusCode.Unauthorized, serviceOnRunnerRoute.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, runnerOnRecordRoute.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, serviceOnRecordRoute.StatusCode);
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_manifest_that_differs_from_the_pin_or_that_no_release_pins_is_a_409_that_names_the_package_and_stores_nothing()
    {
        await PrepareAsync();
        var (id, tenantDatabase) = await AddTenantAsync();
        try
        {
            await new CustomerHostedReleases().ActivateOwnAsync(tenantDatabase, _sumManifest, CancellationToken);
            await using var factory = Start();
            using var admin = Client(factory, id).SignedInAs("admin");
            var runner = await CreateRunnerAsync(admin, id);
            using var client = Client(factory, id);

            using var different = await RegisterAsync(client, runner.Token, Body(_multiplyManifest));
            using var otherVersion = await RegisterAsync(client, runner.Token, Body(CustomerHostedReleases.ToolsManifest("1.0.1", "sum")));
            using var mixed = await RegisterAsync(client, runner.Token, Body(_sumManifest, _multiplyManifest));

            Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, otherVersion.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
            var differentTitle = await TitleAsync(different);
            var otherVersionTitle = await TitleAsync(otherVersion);
            Assert.Contains("'tools' version 1.0.0", differentTitle, StringComparison.Ordinal);
            Assert.Contains("'tools' version 1.0.1", otherVersionTitle, StringComparison.Ordinal);
            Assert.Contains("'tools' version 1.0.0", await TitleAsync(mixed), StringComparison.Ordinal);
            Assert.Equal([], await StoredPackagesAsync(admin));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    [Fact]
    public async Task A_manifest_equal_to_the_customer_hosted_pin_is_a_204_that_the_list_shows_once_even_after_a_repeat()
    {
        await PrepareAsync();
        var (id, tenantDatabase) = await AddTenantAsync();
        try
        {
            await new CustomerHostedReleases().ActivateOwnAsync(tenantDatabase, _sumManifest, CancellationToken);
            await using var factory = Start();
            using var admin = Client(factory, id).SignedInAs("admin");
            var runner = await CreateRunnerAsync(admin, id);
            using var client = Client(factory, id);

            using var first = await RegisterAsync(client, runner.Token, Body(_sumManifest));
            using var repeated = await RegisterAsync(client, runner.Token, Body(_sumManifest, _sumManifest));

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.Equal([("tools", "1.0.0")], await StoredPackagesAsync(admin));
        }
        finally
        {
            await DeleteRealmAsync(id);
        }
    }

    private static string Body(params string[] manifests) =>
        $$"""{ "packages": [{{string.Join(", ", manifests.Select(manifest => $$"""{ "manifest": {{manifest}} }"""))}}] }""";

    // The request carries no CSRF header, because a runner's request needs none. A null token sends no Authorization header, and a null body sends no body.
    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string? token, string? body, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Registration, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, bool csrf, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<string> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("title").GetString()!;

    // The packages of the administrator's one runner list, as id and version.
    private static async Task<List<(string Id, string Version)>> StoredPackagesAsync(HttpClient admin)
    {
        using var response = await SendAsync(admin, HttpMethod.Get, Runners, csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        return
        [
            .. items.EnumerateArray()
                .SelectMany(item => item.GetProperty("packages").EnumerateArray())
                .Select(package => (package.GetProperty("id").GetString()!, package.GetProperty("version").GetString()!)),
        ];
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
            builder.UseTestAuthentication(new TestIdentity("admin", "Admin", AdministratorRole) { SignedInAgo = TimeSpan.Zero });
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(_clock));
        });
    }

    // Creates the platform database. The tenants are added before the server starts, so it finds them.
    private async Task PrepareAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _platform = await CellTestPlatform.CreateAsync(database, new Uri(_baseAddress, "realms/axis"), CancellationToken);
    }

    // Makes a tenant and its realm. The tenant database also gets the process tables, which the registration check reads.
    private async Task<(string Id, string Database)> AddTenantAsync(string? serviceAudience = null)
    {
        var id = $"pr-{Guid.NewGuid():N}"[..16];
        const string usersSecret = "users-secret-1";
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(id, "Acme", new TenantRealmSecrets("platform-secret-1", ActivationSecret, usersSecret, "agent-secret-1"), new Dictionary<string, string>(), CancellationToken);
        try
        {
            var tenantDatabase = await CellTestPlatform.AddRealmTenantAsync(
                database, _platform, id, "active", _baseAddress, usersSecret, CancellationToken, ActivationSecret, $"{id}.vn.example.test", serviceAudience: serviceAudience);
            await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(tenantDatabase).Options))
            {
                await processes.Database.MigrateAsync(CancellationToken);
            }

            await PostgreSqlServer.GrantRuntimeRoleAsync(tenantDatabase, CancellationToken);
            return (id, tenantDatabase);
        }
        catch
        {
            await DeleteRealmAsync(id);
            throw;
        }
    }

    // Creates a runner as the tenant's administrator, and signs in as its client.
    private async Task<RunnerCredential> CreateRunnerAsync(HttpClient admin, string realm)
    {
        using var created = await SendAsync(admin, HttpMethod.Post, Runners, csrf: true, new { name = "Plant" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var clientId = body.GetProperty("clientId").GetString()!;
        var token = await TokenAsync(realm, clientId, body.GetProperty("clientSecret").GetString()!);
        return new RunnerCredential(body.GetProperty("id").GetGuid(), token);
    }

    private async Task<string> TokenAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("access_token").GetString()!;
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

    // Creates a client in the realm through the master realm's administration. Its access token has the audience and no other.
    private async Task CreateClientAsync(string realm, string clientId, string secret, string audience)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, $"admin/realms/{realm}/clients"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        request.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["clientId"] = clientId,
            ["enabled"] = true,
            ["protocol"] = "openid-connect",
            ["publicClient"] = false,
            ["clientAuthenticatorType"] = "client-secret",
            ["secret"] = secret,
            ["standardFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = false,
            ["serviceAccountsEnabled"] = true,
            ["protocolMappers"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = "audience",
                    ["protocol"] = "openid-connect",
                    ["protocolMapper"] = "oidc-audience-mapper",
                    ["config"] = new Dictionary<string, string>
                    {
                        ["included.custom.audience"] = audience,
                        ["access.token.claim"] = "true",
                        ["id.token.claim"] = "false",
                    },
                },
            },
        });
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task DeleteRealmAsync(string realm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    private sealed record RunnerCredential(Guid Id, string Token);

    /// <summary>The system clock moved by <see cref="Advance"/>, so real tokens are valid until it moves forward.</summary>
    private sealed class TestClock : TimeProvider
    {
        private TimeSpan _offset;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;

        public void Advance(TimeSpan time) => _offset += time;
    }
}
