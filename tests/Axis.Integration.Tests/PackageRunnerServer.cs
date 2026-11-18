using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The server and the tenants that the tests of the customer-hosted runner routes share, see
/// <c>docs/reference/extension-packages.md#routes</c>: a platform database, tenants with a Keycloak realm each, runners that sign in with their
/// client's token, and the requests of a runner and of a tenant administrator. A test makes the realms of its tenants with unique names and
/// deletes them at the end.
/// </summary>
internal sealed class PackageRunnerServer(PostgreSqlFixture database, KeycloakFixture keycloak) : IDisposable
{
    public const string Runners = "/api/tenant/package-runners";
    public const string Registration = "/api/package-runner/packages";
    public const string ServiceAudience = "axis-services";

    private const string AdministratorRole = "tenant-administrator";
    private const string ActivationSecret = "activation-secret-1";

    private readonly HttpClient _http = new();
    private Uri _baseAddress = null!;
    private string _platform = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>The clock of the server, which a test moves to expire the tokens.</summary>
    public TestClock Clock { get; } = new();

    public void Dispose() => _http.Dispose();

    // The request carries no CSRF header, because a runner's request needs none. A null token sends no Authorization header, and a null body sends no body.
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, string? token, string? body, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
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

    /// <summary>The body of a registration of the packages with <paramref name="manifests"/>.</summary>
    public static string RegistrationBody(params string[] manifests) =>
        $$"""{ "packages": [{{string.Join(", ", manifests.Select(manifest => $$"""{ "manifest": {{manifest}} }"""))}}] }""";

    /// <summary>Registers the packages with <paramref name="manifests"/> as the runner whose token is <paramref name="token"/>.</summary>
    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string? token, params string[] manifests) =>
        PostAsync(client, Registration, token, RegistrationBody(manifests));

    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, CancellationToken);
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, bool csrf, object? body = null)
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

    public static HttpClient Client(WebApplicationFactory<Program> factory, string id) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{id}.vn.example.test"), AllowAutoRedirect = false });

    public WebApplicationFactory<Program> Start()
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
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
        });
    }

    // Creates the platform database. The tenants are added before the server starts, so it finds them.
    public async Task PrepareAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _platform = await CellTestPlatform.CreateAsync(database, new Uri(_baseAddress, "realms/axis"), CancellationToken);
    }

    // Makes a tenant and its realm. The tenant database also gets the process tables, which the registration check and the take read.
    public async Task<(string Id, string Database)> AddTenantAsync(string? serviceAudience = null)
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

    // Runs the next due package.call item of the tenant on a worker that holds no package, as the worker does. Returns null when no item is due.
    public static async Task<WorkItemOutcome?> RunPackageCallItemAsync(string tenantId, string tenantDatabase)
    {
        var runtime = PostgreSqlServer.RuntimeConnectionString(tenantDatabase);
        using var packages = new TemporaryFolder();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.vn.example.test",
            [$"Tenants:{tenantId}:ConnectionString"] = runtime,
            [PackageCatalog.PathSetting] = packages.Path,
            [PackageIsolation.IsolationSetting] = "inProcess",
        }).Build();
        await using var worker = new ServiceCollection()
            .AddLogging()
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory })
            .BuildServiceProvider();
        await using var connection = new NpgsqlConnection(runtime);
        await connection.OpenAsync(CancellationToken);
        var item = await WorkItemQueue.ClaimAsync(connection, tenantId, [ProcessStarts.PackageCallWorkItemKind], TimeSpan.FromSeconds(30), CancellationToken);
        return item is null ? null : await worker.GetRequiredService<WorkItemRunner>().ExecuteAsync(item, CancellationToken);
    }

    // Creates a runner as the tenant's administrator, and signs in as its client.
    public async Task<RunnerCredential> CreateRunnerAsync(HttpClient admin, string realm)
    {
        using var created = await SendAsync(admin, HttpMethod.Post, Runners, csrf: true, new { name = "Plant" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var clientId = body.GetProperty("clientId").GetString()!;
        var token = await TokenAsync(realm, clientId, body.GetProperty("clientSecret").GetString()!);
        return new RunnerCredential(body.GetProperty("id").GetGuid(), token);
    }

    public async Task<string> TokenAsync(string realm, string clientId, string secret)
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
    public async Task CreateClientAsync(string realm, string clientId, string secret, string audience)
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

    public async Task DeleteRealmAsync(string realm)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await MasterTokenAsync());
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    public sealed record RunnerCredential(Guid Id, string Token);

    /// <summary>The system clock moved by <see cref="Advance"/>, so real tokens are valid until it moves forward.</summary>
    public sealed class TestClock : TimeProvider
    {
        private TimeSpan _offset;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;

        public void Advance(TimeSpan time) => _offset += time;
    }
}
