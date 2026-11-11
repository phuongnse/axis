using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The MCP server reads the client's row in <c>axis.mcp_clients</c> on every call, see docs/reference/mcp-server.md#sign-in. It
/// refuses a token issued at or before the time its client's consent was revoked, and it records the client's last use at most once a
/// minute. The server is tenant <c>a</c> on a real PostgreSQL database, against the development realm's Keycloak.
/// </summary>
public sealed class McpClientUseTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Host = "localhost";
    private const string Version = "2025-11-25";

    private readonly string _applications = Directory.CreateTempSubdirectory("axis-mcp-client-use-").FullName;
    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_issued_at_or_before_the_revocation_is_refused_and_one_issued_after_it_passes()
    {
        var token = await RequestTokenAsync();
        var issuedAt = new DateTimeOffset(new JwtSecurityTokenHandler().ReadJwtToken(token).IssuedAt, TimeSpan.Zero).ToUnixTimeSeconds();

        await SetRevokedAtAsync(issuedAt + 1);
        using var refused = await InitializeAsync(token);
        await SetRevokedAtAsync(issuedAt);
        using var refusedAtTheSameSecond = await InitializeAsync(token);
        await SetRevokedAtAsync(issuedAt - 1);
        using var passed = await InitializeAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.StartsWith("Bearer resource_metadata=", refused.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, refusedAtTheSameSecond.StatusCode);
        Assert.StartsWith("Bearer resource_metadata=", refusedAtTheSameSecond.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
    }

    [Fact]
    public async Task A_successful_call_records_the_last_use_and_a_later_call_writes_it_again_only_after_a_minute()
    {
        var token = await RequestTokenAsync();
        await AdminAsync($"""DELETE FROM "axis"."mcp_clients" WHERE "client_id" = '{KeycloakFixture.McpClientId}'""");

        using var first = await InitializeAsync(token);
        var recorded = await LastUsedAtAsync();
        using var second = await InitializeAsync(token);
        var unchanged = await LastUsedAtAsync();
        await AdminAsync($"""UPDATE "axis"."mcp_clients" SET "last_used_at" = now() - interval '2 minutes' WHERE "client_id" = '{KeycloakFixture.McpClientId}'""");
        var stale = await LastUsedAtAsync();
        using var third = await InitializeAsync(token);
        var renewed = await LastUsedAtAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(recorded);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(recorded, unchanged);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.True(renewed > stale, $"The last use stayed at {stale:O}.");
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        var platform = await database.CreateDatabaseAsync("mcp_client_use", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(database.ConnectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Tenants:a:Oidc:Authority", _authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", _applications);
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{Host}"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Directory.Delete(_applications, recursive: true);
    }

    private Task<string> RequestTokenAsync() =>
        McpTestCalls.RequestTokenAsync(_authority!, "author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret, CancellationToken);

    private Task<HttpResponseMessage> InitializeAsync(string token) =>
        McpTestCalls.PostAsync(_client!, Host, token, McpTestCalls.Initialize(Version), null, null, null, CancellationToken);

    private async Task AdminAsync(string sql) =>
        await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, sql, CancellationToken);

    private Task SetRevokedAtAsync(long unixSeconds) =>
        AdminAsync(
            $"""
            INSERT INTO "axis"."mcp_clients" ("client_id", "revoked_at")
            VALUES ('{KeycloakFixture.McpClientId}', to_timestamp({unixSeconds.ToString(CultureInfo.InvariantCulture)}))
            ON CONFLICT ("client_id") DO UPDATE SET "revoked_at" = EXCLUDED."revoked_at"
            """);

    private async Task<DateTime?> LastUsedAtAsync() =>
        (DateTime?)await PostgreSqlServer.AdminScalarAsync(
            database.ConnectionString,
            $"""SELECT "last_used_at" FROM "axis"."mcp_clients" WHERE "client_id" = '{KeycloakFixture.McpClientId}'""",
            CancellationToken);
}
