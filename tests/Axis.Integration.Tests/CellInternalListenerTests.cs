using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The internal listener of a cell, with real Kestrel, Keycloak and PostgreSQL. The server has the public HTTPS endpoint
/// <c>Https</c> and the endpoint <c>Internal</c>, and <c>Cell:InternalClientCa</c> names the test's authority. Only the
/// internal listener asks for a client certificate, and it still needs an operator realm token. The certificates are made for
/// the test: an authority, a server certificate for <c>127.0.0.1</c> and <c>localhost</c>, a client certificate of the
/// authority and a client certificate of a second authority.
/// </summary>
public sealed class CellInternalListenerTests(PostgreSqlFixture database, KeycloakFixture keycloak)
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string OperatorRole = "cell-operator";
    private const string CallerClientId = "ops-caller";
    private const string ClientSecret = "ops-development-secret";
    private const string ServerUsage = "1.3.6.1.5.5.7.3.1";
    private const string ClientUsage = "1.3.6.1.5.5.7.3.2";

    private readonly HttpClient _http = new();
    private readonly string _realm = $"ops-{Guid.NewGuid():N}"[..16];
    private readonly string _folder = Directory.CreateTempSubdirectory("axis-internal-listener-").FullName;
    private Uri _baseAddress = null!;
    private X509Certificate2 _authority = null!;
    private X509Certificate2 _client = null!;
    private X509Certificate2 _strangerClient = null!;

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
            clients = new[]
            {
                new
                {
                    clientId = CallerClientId,
                    enabled = true,
                    publicClient = false,
                    secret = ClientSecret,
                    serviceAccountsEnabled = true,
                    standardFlowEnabled = false,
                    directAccessGrantsEnabled = false,
                },
            },
            users = new[]
            {
                new
                {
                    username = $"service-account-{CallerClientId}",
                    enabled = true,
                    serviceAccountClientId = CallerClientId,
                    realmRoles = new[] { OperatorRole },
                },
            },
        });
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        _authority = NewAuthority("CN=Axis test internal CA");
        using var stranger = NewAuthority("CN=Axis test stranger CA");
        _client = Issue(_authority, "CN=axis-controlplane", ClientUsage, dnsName: null, address: null);
        _strangerClient = Issue(stranger, "CN=axis-controlplane", ClientUsage, dnsName: null, address: null);
    }

    public async ValueTask DisposeAsync()
    {
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{_realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
        _client.Dispose();
        _strangerClient.Dispose();
        _authority.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_client_certificate_of_the_authority_and_an_operator_token_are_answered_on_the_internal_listener()
    {
        await using var server = await StartAsync();
        using var client = CreateClient(_client);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var response = await GetAsync(client, server.InternalUri("/api/cell/tenants"), token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(["a", "b", "c"], items.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
    }

    [Fact]
    public async Task A_request_without_a_client_certificate_fails_on_the_internal_listener()
    {
        await using var server = await StartAsync();
        using var client = CreateClient(certificate: null);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => GetAsync(client, server.InternalUri("/api/cell/tenants"), token));
    }

    [Fact]
    public async Task A_client_certificate_of_another_authority_fails_on_the_internal_listener()
    {
        await using var server = await StartAsync();
        using var client = CreateClient(_strangerClient);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => GetAsync(client, server.InternalUri("/api/cell/tenants"), token));
    }

    [Fact]
    public async Task A_valid_client_certificate_with_a_tenant_realm_token_answers_401_on_the_internal_listener()
    {
        await using var server = await StartAsync();
        using var client = CreateClient(_client);
        var tenantRealmToken = await TokenAsync("axis", KeycloakFixture.PurchasingClientId, KeycloakFixture.PurchasingClientSecret);

        using var response = await GetAsync(client, server.InternalUri("/api/cell/tenants"), tenantRealmToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_public_https_listener_answers_health_live_to_a_client_without_a_certificate()
    {
        await using var server = await StartAsync();
        using var client = CreateClient(certificate: null);

        using var response = await client.GetAsync(server.PublicUri("/health/live"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri address, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, CancellationToken);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static X509Certificate2 NewAuthority(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var authority = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
        return X509CertificateLoader.LoadPkcs12(authority.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>A certificate for <paramref name="usage"/> that <paramref name="authority"/> signed, with its private key.</summary>
    private static X509Certificate2 Issue(X509Certificate2 authority, string subject, string usage, string? dnsName, IPAddress? address)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], false));
        if (dnsName is not null || address is not null)
        {
            var names = new SubjectAlternativeNameBuilder();
            if (dnsName is not null)
            {
                names.AddDnsName(dnsName);
            }

            if (address is not null)
            {
                names.AddIpAddress(address);
            }

            request.CertificateExtensions.Add(names.Build());
        }

        using var issued = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(8));
        return X509CertificateLoader.LoadPkcs12(issued.CopyWithPrivateKey(key).Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>A client that trusts the test authority for the server's certificate and offers <paramref name="certificate"/>, if any.</summary>
    private HttpClient CreateClient(X509Certificate2? certificate)
    {
        var ssl = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, server, _, _) =>
            {
                if (server is null)
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.CustomTrustStore.Add(_authority);
                return chain.Build(X509CertificateLoader.LoadCertificate(server.GetRawCertData()));
            },
        };
        if (certificate is not null)
        {
            ssl.ClientCertificates = [certificate];
        }

        return new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, SslOptions = ssl, PooledConnectionLifetime = TimeSpan.Zero });
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

    private async Task<RunningServer> StartAsync()
    {
        var identityProvider = await keycloak.StartAsync();
        var platform = await CellTestPlatform.CreateAsync(database, identityProvider, CancellationToken);

        var authorityPath = Path.Combine(_folder, "ca.pem");
        var certificatePath = Path.Combine(_folder, "server.pem");
        var keyPath = Path.Combine(_folder, "server.key");
        await File.WriteAllTextAsync(authorityPath, _authority.ExportCertificatePem(), CancellationToken);
        using (var server = Issue(_authority, "CN=localhost", ServerUsage, "localhost", IPAddress.Loopback))
        {
            await File.WriteAllTextAsync(certificatePath, server.ExportCertificatePem(), CancellationToken);
            await File.WriteAllTextAsync(keyPath, server.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem(), CancellationToken);
        }

        var internalPort = FreePort();
        var publicPort = FreePort();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:OperatorRealm", _realm);
            builder.UseSetting("Cell:OperatorRole", OperatorRole);
            builder.UseSetting("Cell:InternalUrl", "https://127.0.0.1");
            builder.UseSetting("Cell:InternalClientCa", authorityPath);
            builder.UseSetting("IdentityProvider:Url", _baseAddress.ToString());
            builder.UseSetting("Templates:Path", BuiltInApplications.Path);
            builder.UseSetting("Kestrel:Endpoints:Internal:Url", $"https://127.0.0.1:{internalPort}");
            builder.UseSetting("Kestrel:Endpoints:Internal:Certificate:Path", certificatePath);
            builder.UseSetting("Kestrel:Endpoints:Internal:Certificate:KeyPath", keyPath);
            builder.UseSetting("Kestrel:Endpoints:Https:Url", $"https://localhost:{publicPort}");
            builder.UseSetting("Kestrel:Endpoints:Https:Certificate:Path", certificatePath);
            builder.UseSetting("Kestrel:Endpoints:Https:Certificate:KeyPath", keyPath);
        });
        factory.UseKestrel();
        factory.StartServer();
        return new RunningServer(factory, internalPort, publicPort);
    }

    private sealed class RunningServer(WebApplicationFactory<Program> factory, int internalPort, int publicPort) : IAsyncDisposable
    {
        public Uri InternalUri(string path) => new($"https://127.0.0.1:{internalPort}{path}");

        public Uri PublicUri(string path) => new($"https://localhost:{publicPort}{path}");

        public ValueTask DisposeAsync() => factory.DisposeAsync();
    }
}
