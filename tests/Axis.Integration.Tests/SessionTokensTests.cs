using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Admin;
using Axis.Data.Records;
using Axis.Server.Authentication;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The platform client's refresh token that a standard tenant's session keeps, see <c>docs/reference/security.md#sign-in-flow</c>. The
/// realm is made by <see cref="TenantRealm"/> in a real Keycloak, because only it has the client <see cref="RealmAgentClient.ClientId"/>,
/// whose audience the refreshed access token must hold. Tenant <c>a</c> has a data key. After a sign-in the session row holds the token
/// encrypted and no response carries it. A refresh gives an access token of the person and stores the token that the realm returns. A
/// token copied to another session row does not decrypt, so that row refreshes nothing.
/// </summary>
public sealed class SessionTokensTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string AuthenticatorSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string PlatformSecret = "platform-secret-1";
    private const string Tenant = "a";

    // The realm's client axis accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private readonly string _realm = $"t{Guid.NewGuid():N}"[..16];
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly string _dataKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly List<string> _serverResponses = [];
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri _baseAddress = null!;
    private string _adminToken = null!;
    private string _schemaConnection = null!;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => new(_baseAddress, $"realms/{_realm}");

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_platform_sign_in_keeps_the_refresh_token_encrypted_in_the_session_row_and_no_response_carries_it()
    {
        using var client = await SignInAsync();

        var row = await ReadSessionAsync();
        Assert.NotNull(row.RefreshToken);
        var token = await Values().DecryptTextAsync(SessionTokens.Scope(Tenant, row.IdHash), SessionTokens.FieldName, row.RefreshToken, CancellationToken);
        Assert.NotEmpty(token);
        Assert.Equal(-1, row.RefreshToken.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(token)));
        Assert.NotEmpty(_serverResponses);
        foreach (var response in _serverResponses)
        {
            Assert.DoesNotContain(token, response, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh_token", response, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", response, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Two_refreshes_in_a_row_each_give_an_access_token_of_the_person_for_the_agent_host_and_store_a_new_token()
    {
        using var client = await SignInAsync();
        var row = await ReadSessionAsync();
        var tokens = Factory.Services.GetRequiredService<SessionTokens>();
        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(_schemaConnection));
        await connection.OpenAsync(CancellationToken);

        var stored = row.RefreshToken;
        for (var refresh = 0; refresh < 2; refresh++)
        {
            var access = (await tokens.RefreshAsync(connection, Tenant, null, row.IdHash, CancellationToken)).AccessToken;

            Assert.NotNull(access);
            var token = new JsonWebToken(access);
            Assert.Equal(row.UserId, token.Subject);
            Assert.Contains(RealmAgentClient.ClientId, token.Audiences);
            var after = (await ReadSessionAsync()).RefreshToken;
            Assert.NotNull(after);
            Assert.NotEqual(stored, after);
            stored = after;
        }
    }

    [Fact]
    public async Task A_token_copied_to_another_session_row_does_not_decrypt_so_that_row_refreshes_nothing()
    {
        using var client = await SignInAsync();
        var row = await ReadSessionAsync();
        var copy = SHA256.HashData(RandomNumberGenerator.GetBytes(32));
        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(_schemaConnection));
        await connection.OpenAsync(CancellationToken);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.sessions (id_hash, user_id, display_name, roles, signed_in_at, last_request_at, refresh_token)
            VALUES (@hash, @user, 'Copy', ARRAY[]::text[], now(), now(), @token)
            """,
            connection))
        {
            insert.Parameters.AddWithValue("hash", copy);
            insert.Parameters.AddWithValue("user", row.UserId);
            insert.Parameters.AddWithValue("token", row.RefreshToken!);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        var access = (await Factory.Services.GetRequiredService<SessionTokens>().RefreshAsync(connection, Tenant, null, copy, CancellationToken)).AccessToken;

        Assert.Null(access);
        Assert.Null(await ReadRefreshTokenAsync(copy));
        Assert.NotNull(await ReadRefreshTokenAsync(row.IdHash));
    }

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _adminToken = await MasterTokenAsync();
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(_realm, "Acme", new TenantRealmSecrets(PlatformSecret, "activation-secret-1", "users-secret-1", "agent-secret-1"), new Dictionary<string, string>(), CancellationToken);
        await ConfigureClientAsync();
        await CreateUserAsync();

        _schemaConnection = await database.CreateDatabaseAsync("sessiontokens", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_schemaConnection, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_schemaConnection);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", "localhost");
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_schemaConnection));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", _schemaConnection);
            builder.UseSetting($"Tenants:{Tenant}:Oidc:Authority", Authority.ToString());
            builder.UseSetting($"Tenants:{Tenant}:Oidc:ClientId", "axis");
            builder.UseSetting($"Tenants:{Tenant}:Oidc:ClientSecret", PlatformSecret);
            builder.UseSetting($"Tenants:{Tenant}:DataKeys:1", _dataKey);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{_realm}");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    private SensitiveValues Values() =>
        new(new TenantKeys(new ConfigurationTenantKeySource(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"Tenants:{Tenant}:DataKeys:1"] = _dataKey })
            .Build())));

    /// <summary>Signs in through the platform client at the realm's login form, and records every response of the server.</summary>
    private async Task<HttpClient> SignInAsync()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var callback = await KeycloakSignIn.LogInAsync(
            client, Authority, "pat", _serverAddress, RecordAsync, returnUrl: "/", code: await NextAuthenticatorCodeAsync(), password: _password);
        using var signedIn = await RecordAsync(await client.GetAsync(callback.PathAndQuery, CancellationToken));
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        using var me = await RecordAsync(await client.GetAsync("/api/me", CancellationToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return client;
    }

    private async Task<HttpResponseMessage> RecordAsync(HttpResponseMessage response)
    {
        _serverResponses.Add($"{response.Headers}\n{response.Content.Headers}\n{await response.Content.ReadAsStringAsync(CancellationToken)}");
        return response;
    }

    // The realm accepts only the code of the current 30 seconds, so the sign-in waits for a window with time left in it.
    private static async Task<string> NextAuthenticatorCodeAsync()
    {
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 >= 27)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        }

        return DevelopmentTotp.CodeFromSecret(AuthenticatorSecret, DateTimeOffset.UtcNow);
    }

    private async Task<SessionRecord> ReadSessionAsync()
    {
        await using var connection = new NpgsqlConnection(_schemaConnection);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT id_hash, user_id, refresh_token FROM axis.sessions ORDER BY signed_in_at LIMIT 1", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return new SessionRecord(
            reader.GetFieldValue<byte[]>(0),
            reader.GetString(1),
            await reader.IsDBNullAsync(2, CancellationToken) ? null : reader.GetFieldValue<byte[]>(2));
    }

    private async Task<byte[]?> ReadRefreshTokenAsync(byte[] idHash) =>
        await PostgreSqlServer.AdminScalarAsync(
            _schemaConnection, $"SELECT refresh_token FROM axis.sessions WHERE id_hash = '\\x{Convert.ToHexString(idHash)}'", CancellationToken) as byte[];

    // The axis client accepts the test host's callback.
    private async Task ConfigureClientAsync()
    {
        var axis = (await GetJsonAsync($"admin/realms/{_realm}/clients?clientId=axis")).EnumerateArray().Single();
        var uuid = axis.GetProperty("id").GetString()!;
        var representation = JsonNode.Parse(axis.GetRawText())!.AsObject();
        representation["redirectUris"] = new JsonArray("http://localhost:5206/*");
        using var updated = await SendAsync(HttpMethod.Put, $"admin/realms/{_realm}/clients/{uuid}", representation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    // The account signs in with a password and a code from an authenticator.
    private async Task CreateUserAsync()
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{_realm}/users", new
        {
            username = "pat",
            enabled = true,
            emailVerified = true,
            email = "pat@example.test",
            firstName = "Pat",
            lastName = "Example",
            credentials = new object[]
            {
                new { type = "password", value = _password, temporary = false },
                new
                {
                    type = "otp",
                    secretData = $$"""{"value":"{{AuthenticatorSecret}}"}""",
                    credentialData = """{"subType":"totp","digits":6,"period":30,"algorithm":"HmacSHA1","counter":0,"secretEncoding":"BASE32"}""",
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, CancellationToken);
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

    private sealed record SessionRecord(byte[] IdHash, string UserId, byte[]? RefreshToken);
}
