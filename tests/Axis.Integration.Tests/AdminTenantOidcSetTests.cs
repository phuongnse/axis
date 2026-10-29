using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Admin;
using Axis.Administration;
using Axis.Configuration.Tests;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
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

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_audiences_are_stored_and_the_record_names_them()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = await scenario.RowAsync(id);

        var result = await scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--endpoint-audience", "axis-endpoints", "--service-audience", "axis-services"],
            extra: _directory);

        Assert.Equal(0, result.Exit);
        Assert.Equal([$"{id} oidc ok"], result.Output);
        Assert.Empty(result.Error);
        var row = await scenario.RowAsync(id);
        Assert.Equal("axis-endpoints", row.EndpointAudience);
        Assert.Equal("axis-services", row.ServiceAudience);
        Assert.Equal(before.Version + 1, row.Version);
        Assert.Equal(before.OidcAuthority, row.OidcAuthority);
        Assert.Equal(before.OidcClientSecret, row.OidcClientSecret);
        var record = Assert.Single(await scenario.AuditRecordsAsync(), record => record.Command == "tenant oidc set");
        Assert.Equal(id, record.TenantId);
        Assert.Equal(["endpointAudience", "serviceAudience"], record.Changed);
        await scenario.AssertNoSecretsAsync();
    }

    [Theory]
    [InlineData("--authority", "https://idp.example.test/realms/x")]
    [InlineData("--client-id", "x")]
    [InlineData("--client-secret", null)]
    [InlineData("--claim", "Email=mail")]
    public async Task A_removed_option_exits_with_2_and_changes_neither_the_entry_nor_the_audit_records(string option, string? value)
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var version = (await scenario.RowAsync(id)).Version;
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;
        string[] args = value is null
            ? ["tenant", "oidc", "set", id, "--endpoint-audience", "axis-endpoints", option]
            : ["tenant", "oidc", "set", id, "--endpoint-audience", "axis-endpoints", option, value];

        // The client secret variable is set, so the old option would have found its value.
        var result = await scenario.RunAsync(args, extra: _directory);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
        Assert.Equal(version, (await scenario.RowAsync(id)).Version);
        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
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
            await scenario.RunAsync(["tenant", "oidc", "set", preview, "--endpoint-audience", "x"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", main, "--profile", "nope"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", "t-unknown", "--endpoint-audience", "x"], extra: _directory),
            await scenario.RunAsync(["tenant", "oidc", "set", main, "--endpoint-audience", "x"]),
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
    public async Task A_client_key_is_added_from_the_variable_or_standard_input_and_retired_and_it_is_stored_encrypted_and_named_by_its_key_id()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var first = NewKeyPem();
        var second = NewKeyPem();
        var firstId = KeyId(first, out var firstPublic);
        var secondId = KeyId(second, out var secondPublic);
        Assert.Equal(ThumbprintOf(first), firstId);
        Assert.NotEqual(firstId, secondId);
        var start = await scenario.RowAsync(id);
        var recordCount = (await scenario.AuditRecordsAsync()).Count;

        // One run turns fapi2 on and adds the first key. The key id and the public key are printed, never the private key.
        var added = await scenario.RunAsync(["tenant", "oidc", "set", id, "--client-key", "--profile", "fapi2"], extra: Key(first));

        Assert.Equal(0, added.Exit);
        Assert.Empty(added.Error);
        Assert.Equal([$"{id} oidc ok", $"{id} client-key {firstId}", .. Lines(firstPublic)], added.Output);
        var row = await scenario.RowAsync(id);
        Assert.Equal(TenantSignInProfiles.Fapi2, row.OidcProfile);
        Assert.Equal(firstId, row.OidcClientKeyId);
        Assert.Null(row.OidcPreviousClientKey);
        Assert.Null(row.OidcPreviousClientKeyId);
        Assert.Equal(first, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.OidcClientKey, row.OidcClientKey!).Reveal());
        Assert.All(AdminScenario.PemBodyLines(first), line => Assert.False(row.OidcClientKey.AsSpan().IndexOf(Encoding.UTF8.GetBytes(line)) >= 0, "The stored client key holds the plain key."));
        Assert.Equal(start.Version + 1, row.Version);
        var records = await scenario.AuditRecordsAsync();
        Assert.Equal(recordCount + 1, records.Count);
        Assert.Equal(["profile", $"clientKey:{firstId}"], records[^1].Changed);
        Assert.Equal((id, "tenant oidc set"), (records[^1].TenantId, records[^1].Command));

        // Without the variable, the key is all of standard input. The newest key comes first and the older one moves to the previous columns.
        var piped = await scenario.RunAsync(["tenant", "oidc", "set", id, "--client-key"], withClientSecret: false, extra: _directory, input: second);

        Assert.Equal(0, piped.Exit);
        Assert.Equal([$"{id} oidc ok", $"{id} client-key {secondId}", .. Lines(secondPublic)], piped.Output);
        var both = await scenario.RowAsync(id);
        Assert.Equal(secondId, both.OidcClientKeyId);
        Assert.Equal(second, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.OidcClientKey, both.OidcClientKey!).Reveal());
        Assert.Equal(firstId, both.OidcPreviousClientKeyId);
        Assert.Equal(row.OidcClientKey, both.OidcPreviousClientKey);
        Assert.Equal(row.Version + 1, both.Version);
        records = await scenario.AuditRecordsAsync();
        Assert.Equal(recordCount + 2, records.Count);
        Assert.Equal([$"clientKey:{secondId}"], records[^1].Changed);

        // Retiring the newest key leaves the other one as the key that the server signs with.
        var retired = await scenario.RunAsync(["tenant", "oidc", "set", id, "--retire-client-key", secondId], extra: _directory);

        Assert.Equal(0, retired.Exit);
        Assert.Equal([$"{id} oidc ok"], retired.Output);
        var one = await scenario.RowAsync(id);
        Assert.Equal(firstId, one.OidcClientKeyId);
        Assert.Equal(row.OidcClientKey, one.OidcClientKey);
        Assert.Null(one.OidcPreviousClientKeyId);
        Assert.Null(one.OidcPreviousClientKey);
        Assert.Equal(both.Version + 1, one.Version);
        records = await scenario.AuditRecordsAsync();
        Assert.Equal(recordCount + 3, records.Count);
        Assert.Equal([$"retireClientKey:{secondId}"], records[^1].Changed);

        // Retiring the older of two keys keeps the newest one.
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", id, "--client-key"], extra: Key(second))).Exit);
        var older = await scenario.RunAsync(["tenant", "oidc", "set", id, "--retire-client-key", firstId], extra: _directory);

        Assert.Equal(0, older.Exit);
        var rest = await scenario.RowAsync(id);
        Assert.Equal(secondId, rest.OidcClientKeyId);
        Assert.Null(rest.OidcPreviousClientKeyId);
        Assert.Equal(second, AdminScenario.Cipher.Decrypt(id, TenantSecretFields.OidcClientKey, rest.OidcClientKey!).Reveal());
        Assert.All(await scenario.AuditRecordsAsync(), entry => Assert.False(AdminScenario.HoldsPemBody(entry.Text, first), "An audit record holds the client key."));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_fapi2_tenant_turns_to_standard_in_the_same_run_that_retires_its_last_client_key()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var pem = NewKeyPem();
        var keyId = KeyId(pem, out _);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", id, "--client-key", "--profile", "fapi2"], extra: Key(pem))).Exit);

        var result = await scenario.RunAsync(["tenant", "oidc", "set", id, "--profile", "standard", "--retire-client-key", keyId], extra: _directory);

        Assert.Equal(0, result.Exit);
        var row = await scenario.RowAsync(id);
        Assert.Equal(TenantSignInProfiles.Standard, row.OidcProfile);
        Assert.Null(row.OidcClientKey);
        Assert.Null(row.OidcClientKeyId);
        Assert.Equal(["profile", $"retireClientKey:{keyId}"], (await scenario.AuditRecordsAsync())[^1].Changed);
    }

    [Fact]
    public async Task A_refused_client_key_call_exits_with_2_and_changes_neither_the_entry_nor_the_audit_records()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var two = scenario.NewTenantId();
        var one = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(two, $"{two}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(one, $"{one}.example.test")).Exit);
        var first = NewKeyPem();
        var second = NewKeyPem();
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", two, "--client-key", "--profile", "fapi2"], extra: Key(first))).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", two, "--client-key"], extra: Key(second))).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "oidc", "set", one, "--client-key", "--profile", "fapi2"], extra: Key(first))).Exit);
        var snapshot = await scenario.SnapshotAsync();
        var records = (await scenario.AuditRecordsAsync()).Count;

        using var rsa = RSA.Create(2048);
        using var wide = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var narrow = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var refusals = new List<(string[] Args, IReadOnlyDictionary<string, string?> Extra, string? Input)>
        {
            // A third key.
            (["tenant", "oidc", "set", two, "--client-key"], Key(NewKeyPem()), null),
            // The same key twice, on a tenant with room and on one without.
            (["tenant", "oidc", "set", one, "--client-key"], Key(first), null),
            (["tenant", "oidc", "set", two, "--client-key"], Key(second), null),
            // The last key of a fapi2 tenant.
            (["tenant", "oidc", "set", one, "--retire-client-key", KeyId(first, out _)], _directory, null),
            // An unknown key id, and a blank one.
            (["tenant", "oidc", "set", two, "--retire-client-key", "no-such-key"], _directory, null),
            (["tenant", "oidc", "set", two, "--retire-client-key", " "], _directory, null),
            // A PEM that is not an EC P-256 private key.
            (["tenant", "oidc", "set", one, "--client-key"], Key("not a pem"), null),
            (["tenant", "oidc", "set", one, "--client-key"], Key(rsa.ExportPkcs8PrivateKeyPem()), null),
            (["tenant", "oidc", "set", one, "--client-key"], Key(wide.ExportPkcs8PrivateKeyPem()), null),
            (["tenant", "oidc", "set", one, "--client-key"], Key(narrow.ExportSubjectPublicKeyInfoPem()), null),
            (["tenant", "oidc", "set", one, "--client-key"], _directory, ""),
            (["tenant", "oidc", "set", one, "--client-key"], _directory, "garbage\n"),
            // A repeated option.
            (["tenant", "oidc", "set", one, "--client-key", "--client-key"], Key(NewKeyPem()), null),
            (["tenant", "oidc", "set", two, "--retire-client-key", "a", "--retire-client-key", "b"], _directory, null),
        };

        foreach (var (args, extra, input) in refusals)
        {
            var refusal = await scenario.RunAsync(args, withClientSecret: false, extra: extra, input: input);
            Assert.Equal(2, refusal.Exit);
            Assert.Empty(refusal.Output);
            Assert.NotEmpty(refusal.Error);
        }

        Assert.Equal(snapshot, await scenario.SnapshotAsync());
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
        await scenario.AssertNoSecretsAsync();
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

    private static Dictionary<string, string?> Key(string pem) =>
        new Dictionary<string, string?> { ["Tenancy:Source"] = "directory", [AdminCommands.ClientKeySetting] = pem };

    private static string NewKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    // The key id and the public key that the command works out for a PEM.
    private static string KeyId(string pem, out string publicKeyPem)
    {
        Assert.True(TenantClientKeys.TryRead(pem, out _, out var keyId, out publicKeyPem));
        return keyId;
    }

    // The RFC 7638 thumbprint, worked out by the token library instead of by the command.
    private static string ThumbprintOf(string pem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(key));
        return Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
    }

    private static string[] Lines(string pem) => pem.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    // Creates the tenant, which gets a realm of its own, then points it at the development realm. The secret stays the generated one.
    private static async Task<AdminResult> CreateAsync(AdminScenario scenario, string id, string host, Uri authority)
    {
        var created = await scenario.CreateAsync(id, host);
        if (created.Exit == 0)
        {
            await scenario.PointSignInAsync(id, authority, KeycloakFixture.ClientId);
        }

        return created;
    }

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

    private static Task RefreshAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

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
        Assert.Equal(0, (await CreateAsync(scenario, id, $"{id}.example.test", authority)).Exit);
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
