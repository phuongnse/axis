using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant oidc set</c> changes the sign-in settings of a main tenant in the directory. A running server with the real
/// Keycloak realm takes the new settings at its next refresh.
/// </summary>
public sealed class AdminTenantOidcSetTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";
    private const string Departments = "/api/apps/PurchaseRequests/entities/Department/records";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static readonly Dictionary<string, string?> _directory = new() { ["Tenancy:Source"] = "directory" };

    private static readonly Uri _address = new("http://localhost:5206");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_secret_and_claim_name_let_a_person_sign_in_after_the_next_refresh()
    {
        var authority = await keycloak.StartAsync();
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(CreateArgs(id, "localhost", authority))).Exit);
        await using var factory = CreateFactory(scenario.Platform);
        using var failing = CreateClient(factory);

        // The secret that the tenant was created with is not the one of the realm's client.
        Assert.False(await TrySignInAsync(failing, authority));

        var result = await scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--client-secret", "--claim", "DisplayName=preferred_username", "--endpoint-audience", "axis-endpoints", "--service-audience", "axis-services"],
            extra: Directory(KeycloakFixture.ClientSecret));
        await RefreshAsync(factory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} oidc ok"], result.Output);
        using var client = CreateClient(factory);
        Assert.True(await TrySignInAsync(client, authority));
        using var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("anna", body.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task The_new_secret_is_stored_encrypted_and_no_output_or_record_holds_it_and_the_record_names_the_settings()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = await scenario.RowAsync(id);
        var secret = $"new-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";

        var result = await scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--client-secret", "--claim", "DisplayName=preferred_username", "--endpoint-audience", "axis-endpoints", "--service-audience", "axis-services"],
            extra: Directory(secret));

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} oidc ok"], result.Output);
        Assert.Empty(result.Error);
        var row = await scenario.RowAsync(id);
        Assert.Equal(secret, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.OidcClientSecret, row.OidcClientSecret).Reveal());
        Assert.False(row.OidcClientSecret.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) >= 0, "The stored client secret holds the plain secret.");
        Assert.Equal("preferred_username", row.ClaimDisplayName);
        Assert.Equal("axis-endpoints", row.EndpointAudience);
        Assert.Equal("axis-services", row.ServiceAudience);
        Assert.Equal(before.Version + 1, row.Version);
        Assert.Equal(before.OidcAuthority, row.OidcAuthority);
        var records = await scenario.AuditRecordsAsync();
        var record = Assert.Single(records, record => record.Command == "tenant oidc set");
        Assert.Equal(id, record.TenantId);
        Assert.Equal(["clientSecret", "claimDisplayName", "endpointAudience", "serviceAudience"], record.Changed);
        Assert.All(records, entry => Assert.DoesNotContain(secret, entry.Text, StringComparison.Ordinal));
        Assert.DoesNotContain(secret, string.Join('\n', [.. result.Output, .. result.Error]), StringComparison.Ordinal);

        // Without the variable, the secret is the first line of standard input.
        var second = $"second-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
        var piped = await scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--client-secret"],
            withClientSecret: false,
            extra: _directory,
            input: new StringReader($"{second}\nignored\n"));

        Assert.Equal(0, piped.Exit);
        var after = await scenario.RowAsync(id);
        Assert.Equal(second, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.OidcClientSecret, after.OidcClientSecret).Reveal());
        Assert.Equal(row.Version + 1, after.Version);
        Assert.All(await scenario.AuditRecordsAsync(), entry => Assert.DoesNotContain(second, entry.Text, StringComparison.Ordinal));

        // An empty secret on standard input is refused and changes nothing.
        var snapshot = await scenario.SnapshotAsync();
        var empty = await scenario.RunAsync(["tenant", "oidc", "set", id, "--client-secret"], withClientSecret: false, extra: _directory, input: new StringReader("\n"));

        Assert.Equal(2, empty.Exit);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_refused_call_exits_with_2_and_changes_neither_the_entry_nor_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var main = scenario.NewTenantId();
        var preview = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(main, $"{main}.example.test")).Exit);
        Assert.Equal(0, (await scenario.RunAsync(AdminScenario.PreviewArgs(preview, main, $"{preview}.example.test"))).Exit);
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;

        var refusals = new[]
        {
            await scenario.RunAsync(["tenant", "oidc", "set", main, "--profile", "fapi2"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", preview, "--client-id", "x"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", main, "--claim", "Nickname=nick"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", "t-unknown", "--client-id", "x"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", main, "--client-id", "x"]),
        };

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(2, refusal.Exit);
            Assert.Empty(refusal.Output);
            Assert.NotEmpty(refusal.Error);
        });
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
    }

    [Fact]
    public async Task A_service_account_token_is_refused_then_accepted_after_the_service_audience_is_set_and_refused_again_after_it_is_removed()
    {
        var authority = await keycloak.StartAsync();
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateServedTenantAsync(scenario, authority);
        await using var factory = CreateFactory(scenario.Platform);
        using var client = factory.CreateClient();
        var token = await RequestTokenAsync(authority);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, id, token));

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", id, "--service-audience", "axis-services"], extra: _directory)).Exit);
        await RefreshAsync(factory);

        Assert.NotEqual(HttpStatusCode.Unauthorized, await StatusAsync(client, id, token));

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", id, "--service-audience", ""], extra: _directory)).Exit);
        await RefreshAsync(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, id, token));
        Assert.Null((await scenario.RowAsync(id)).ServiceAudience);
    }

    private static Dictionary<string, string?> Directory(string secret) =>
        new Dictionary<string, string?> { ["Tenancy:Source"] = "directory", ["AXIS_OIDC_CLIENT_SECRET"] = secret };

    private static string[] CreateArgs(string id, string host, Uri authority) =>
        ["tenant", "create", id, "--host", host, "--oidc-authority", authority.ToString(), "--oidc-client-id", KeycloakFixture.ClientId];

    private static WebApplicationFactory<Program> CreateFactory(string platform) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(platform);

            // An hour, so the timer never races the refresh that the test triggers itself.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _address, AllowAutoRedirect = false });

    private static Task RefreshAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

    // Logs in at Keycloak and completes the callback. True when the server answered with a session cookie.
    private static async Task<bool> TrySignInAsync(HttpClient client, Uri authority)
    {
        var callback = await KeycloakSignIn.LogInAsync(client, authority, "anna", _address);
        try
        {
            using var response = await client.GetAsync(new Uri(callback.PathAndQuery, UriKind.Relative), CancellationToken);
            return response.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string id, string token)
    {
        using var request = Request(Departments, $"{id}.example.test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    private static async Task<string> RequestTokenAsync(Uri authority)
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.ReportingClientId,
            ["client_secret"] = KeycloakFixture.ReportingClientSecret,
        });
        using var response = await client.PostAsync(new Uri($"{authority}/protocol/openid-connect/token"), form, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var tokens = JsonDocument.Parse(body);
        return tokens.RootElement.GetProperty("access_token").GetString()!;
    }

    // A tenant with the purchase request sample's bindings and the sample active.
    private static async Task<string> CreateServedTenantAsync(AdminScenario scenario, Uri authority)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.RunAsync(CreateArgs(id, $"{id}.example.test", authority))).Exit);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = id, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = id,
                    Name = SecretBinding,
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
                });
            await context.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", id])).Exit);
        return id;
    }
}
