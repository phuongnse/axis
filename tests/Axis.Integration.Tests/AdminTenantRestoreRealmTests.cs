using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Axis.Admin;
using Axis.Configuration.Tests;
using static Axis.Integration.Tests.AdminActivateRealmTests;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant restore</c> on a real Keycloak. An archive whose realm is missing gets a new realm named after the new tenant id,
/// with the clients of the active releases and without the providers whose secret bindings have no value yet. An archive whose
/// realm exists keeps it, with its users, and no realm is created. An archive with <c>realm.json</c> restored into a new realm brings its
/// roles, groups and users back, so a person signs in with the old password and authenticator under the same user id. An archive without it
/// gives a realm with no users but service accounts, and a <c>realm.json</c> that is not valid is refused before any tenant work.
/// </summary>
public sealed class AdminTenantRestoreRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Issuer = "Issuer";
    private const string Client = "Client";
    private const string Secret = "Secret";
    private const string CredentialsSetting = "IdentityProvider:DatabaseConnectionString";
    private const string Username = "person@example.test";
    private const string TotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string SignInClient = "restored-sign-in";
    private const string RedirectUri = "http://localhost/callback";

    private static readonly string[] _restoreSteps =
    [
        "check", "entry", "roles", "database", "restore", "grants", "grants", "work", "sessions", "role-check", "realm", "version",
    ];

    private static readonly string[] _finance = ["/finance"];

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;
    private PostgresClientTools _tools = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Dictionary<string, string?> IdentityProvider => new()
    {
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _tools = await PostgresClientTools.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}", null);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task A_backup_restored_where_its_realm_is_missing_gets_a_new_realm_in_step_with_its_active_releases()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", original, Issuer, "--url", new Uri(_baseAddress, "realms/master").AbsoluteUri], extra: AdminScenario.Directory)).Exit);
        foreach (var name in new[] { Client, Secret })
        {
            Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", original, name, "--secret"], input: $"{name}-value-{original}", extra: AdminScenario.Directory)).Exit);
        }

        using var folder = Folder(new Application(), ["sales"], provider: true);
        Assert.Equal(0, (await scenario.RunAsync(["activate", folder.Path, "--tenant", original], identityProvider: _handler)).Exit);
        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(original));
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", original, "--out", file, .. AdminScenario.BackupKeyArgs], path: _tools.PathSetting)).Exit);
        await DeleteRealmAsync(original);
        var restored = scenario.NewTenantId();
        _realms.Add(restored);

        var result = await RestoreAsync(scenario, file, restored, withClientSecret: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_restoreSteps.Select(step => $"{restored} {step} ok"), result.Output);
        Assert.Equal([$"{restored} warning the identity provider 'oidc-issuer' waits for the binding '{Client}', which has no value"], result.Error);
        Assert.Equal(["axis", "axis-activation", "axis-users", "site-sales"], await ClientIdsAsync(restored));
        Assert.Empty(await ProviderAliasesAsync(restored));
        var row = await scenario.RowAsync(restored);
        Assert.Equal(new Uri(_baseAddress, $"realms/{restored}").ToString(), row.OidcAuthority);
        Assert.Equal("axis", row.OidcClientId);
        Assert.Equal("suspended", row.Status);

        // The operator sets the secret bindings, which the archive never held. The next activation adds the provider.
        foreach (var name in new[] { Client, Secret })
        {
            Assert.Equal(0, (await scenario.RunAsync(["tenant", "binding", "set", restored, name, "--secret"], input: $"{name}-value-{restored}", extra: AdminScenario.Directory)).Exit);
        }

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", restored])).Exit);
        var activated = await scenario.RunAsync(["activate", folder.Path, "--tenant", restored], identityProvider: _handler);
        Assert.Equal(0, activated.Exit);
        Assert.Equal(["oidc-issuer"], await ProviderAliasesAsync(restored));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_backup_restored_while_its_realm_exists_keeps_the_realm_and_its_users_and_creates_no_realm()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        await CreateUserAsync(original, "kept-user");
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", original, "--out", file, .. AdminScenario.BackupKeyArgs], path: _tools.PathSetting)).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "suspend", original])).Exit);
        var restored = scenario.NewTenantId();
        _realms.Add(restored);

        var result = await RestoreAsync(scenario, file, restored, withClientSecret: true);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_restoreSteps.Where(step => step != "realm").Select(step => $"{restored} {step} ok"), result.Output);
        Assert.Empty(result.Error);
        Assert.False(await RealmExistsAsync(restored));
        Assert.Equal(["kept-user"], await UsernamesAsync(original, "kept-user"));
        var kept = await scenario.RowAsync(original);
        var row = await scenario.RowAsync(restored);
        Assert.Equal(kept.OidcAuthority, row.OidcAuthority);
        Assert.Equal("axis", row.OidcClientId);
        Assert.Null(row.RealmActivationClientSecret);
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_backup_with_realm_json_restored_into_a_new_realm_lets_a_person_sign_in_as_before_with_the_same_user_id()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        var password = $"Long-unbreached-{Guid.NewGuid():N}-password";
        var userId = await FillRealmAsync(original, password);
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        var backup = await BackupAsync(scenario, original, file, await keycloak.CredentialsConnectionStringAsync());
        Assert.Equal(0, backup.Exit);
        await DeleteRealmAsync(original);
        var restored = scenario.NewTenantId();
        _realms.Add(restored);

        var result = await RestoreAsync(scenario, file, restored, withClientSecret: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_restoreSteps.Select(step => $"{restored} {step} ok"), result.Output);
        var user = await GetAsync(restored, $"users/{userId}");
        Assert.Equal(Username, user.GetProperty("username").GetString());
        var mappings = await GetAsync(restored, $"users/{userId}/role-mappings");
        var realmRoles = mappings.GetProperty("realmMappings").EnumerateArray().Select(role => role.GetProperty("name").GetString()!).ToList();
        Assert.Contains($"default-roles-{restored.ToLowerInvariant()}", realmRoles);
        Assert.Contains("approver", realmRoles);
        Assert.DoesNotContain($"default-roles-{original.ToLowerInvariant()}", realmRoles);
        var clientMappings = mappings.GetProperty("clientMappings");
        Assert.Equal(["reviewer"], clientMappings.GetProperty("axis").GetProperty("mappings").EnumerateArray().Select(role => role.GetProperty("name").GetString()!));
        Assert.False(clientMappings.TryGetProperty("site-gone", out _));
        Assert.Equal(["/finance"], (await GetAsync(restored, $"users/{userId}/groups")).EnumerateArray().Select(group => group.GetProperty("path").GetString()!));

        await AddClientAsync(restored);
        Assert.Equal(userId, await SignInAsync(restored, password));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_backup_without_realm_json_restored_into_a_new_realm_has_no_users_but_service_accounts()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        await CreateUserAsync(original, "left-behind");
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", original, "--out", file, .. AdminScenario.BackupKeyArgs], path: _tools.PathSetting)).Exit);
        await DeleteRealmAsync(original);
        var restored = scenario.NewTenantId();
        _realms.Add(restored);

        var result = await RestoreAsync(scenario, file, restored, withClientSecret: false);

        Assert.Equal(0, result.Exit);
        Assert.Equal(_restoreSteps.Select(step => $"{restored} {step} ok"), result.Output);
        var usernames = (await GetAsync(restored, "users?briefRepresentation=true&max=1000")).EnumerateArray().Select(user => user.GetProperty("username").GetString()!).ToList();
        Assert.DoesNotContain("left-behind", usernames);
        Assert.All(usernames, username => Assert.StartsWith("service-account-", username, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_realm_json_that_is_not_valid_refuses_a_restore_into_a_new_realm_and_writes_no_tenant_entry()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var original = await CreateTenantAsync(scenario);
        using var archive = new TemporaryFolder();
        var file = Path.Combine(archive.Path, "backup.tar");
        Assert.Equal(0, (await BackupAsync(scenario, original, file, await keycloak.CredentialsConnectionStringAsync())).Exit);
        var broken = Path.Combine(archive.Path, "broken.tar");
        WithRealmJson(file, broken, "not json");
        await DeleteRealmAsync(original);
        var restored = scenario.NewTenantId();
        var entries = await scenario.CountEntriesAsync();

        var result = await RestoreAsync(scenario, broken, restored, withClientSecret: false);

        Assert.Equal(2, result.Exit);
        Assert.Equal([$"{restored} check failed: the archive's realm.json is not valid"], result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(entries, await scenario.CountEntriesAsync());
        Assert.False(await RealmExistsAsync(restored));
    }

    [Fact]
    public void A_realm_json_entry_larger_than_64_MiB_is_refused_without_being_read()
    {
        var (document, problem) = TenantRealmImport.Read(new MemoryStream([]), TenantRealmImport.MaxBytes + 1);

        Assert.Null(document);
        Assert.Equal("the archive's realm.json is larger than 64 MiB", problem);
        Assert.Equal(TenantRealmImport.TooLarge, problem);
    }

    [Theory]
    [InlineData("""{"realm":"a","roles":{"realm":[],"client":{}},"groups":[],"users":[]}""", true)]
    [InlineData("""{"realm":"a","roles":{"realm":[],"client":{}},"groups":[],"users":[{"id":"1","username":"x"}]}""", true)]
    [InlineData("""{"realm":"a","roles":{"realm":[],"client":{}},"groups":[],"users":[{"username":"x"}]}""", false)]
    [InlineData("""{"realm":"","roles":{"realm":[],"client":{}},"groups":[],"users":[]}""", false)]
    [InlineData("""{"realm":"a","roles":{"realm":[{"description":"no name"}],"client":{}},"groups":[],"users":[]}""", false)]
    [InlineData("""{"realm":"a","roles":{"realm":[],"client":{"axis":{}}},"groups":[],"users":[]}""", false)]
    [InlineData("""{"realm":"a","roles":{"realm":[],"client":{}},"groups":{},"users":[]}""", false)]
    [InlineData("""[]""", false)]
    [InlineData("""not json""", false)]
    public void A_realm_json_is_valid_when_it_has_the_shape_that_backup_writes(string text, bool valid)
    {
        var (document, problem) = TenantRealmImport.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), text.Length);

        Assert.Equal(valid, document is not null);
        Assert.Equal(valid ? null : TenantRealmImport.Invalid, problem);
    }

    private Task<AdminResult> BackupAsync(AdminScenario scenario, string id, string file, string credentials)
    {
        var extra = IdentityProvider;
        extra[CredentialsSetting] = credentials;
        return scenario.RunAsync(
            ["tenant", "backup", id, "--out", file, .. AdminScenario.BackupKeyArgs],
            path: _tools.PathSetting,
            extra: extra,
            identityProvider: _handler);
    }

    // Copies the archive with a realm.json of the given text right after the manifest, as backup writes it.
    private static void WithRealmJson(string source, string destination, string realmJson)
    {
        byte[]? manifest = null;
        byte[]? dump = null;
        using (var stream = File.OpenRead(source))
        using (var decrypted = BackupArchive.Decrypt(stream, AdminScenario.BackupKeyBytes))
        using (var reader = new TarReader(decrypted))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                using var content = new MemoryStream();
                entry.DataStream!.CopyTo(content);
                if (entry.Name == "manifest.json")
                {
                    manifest = content.ToArray();
                }
                else if (entry.Name == "tenant.dump")
                {
                    dump = content.ToArray();
                }
            }
        }

        using var archive = File.Create(destination);
        using var encrypted = BackupArchive.Encrypt(archive, AdminScenario.BackupKeyBytes);
        using var writer = new TarWriter(encrypted, TarEntryFormat.Pax, leaveOpen: true);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json") { DataStream = new MemoryStream(manifest!) });
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "realm.json") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(realmJson)) });
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "tenant.dump") { DataStream = new MemoryStream(dump!) });
    }

    // Fills the realm in one partial import: a realm role, a client role of axis, a group, and a user who holds all three and has a password and an
    // authenticator but no required action. The partial import gives no default role, so the user holds the realm's by name, as a person that the platform
    // invited does. A role of a client named site-gone is mapped to the user, and the restored realm has no such client.
    // Returns the user's id.
    private async Task<string> FillRealmAsync(string realm, string password)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await TokenAsync(http);
        using var imported = await SendAsync(
            http,
            token,
            HttpMethod.Post,
            $"admin/realms/{realm}/partialImport",
            new
            {
                ifResourceExists = "FAIL",
                roles = new
                {
                    realm = new[] { new { name = "approver" } },
                    client = new Dictionary<string, object[]> { ["axis"] = [new { name = "reviewer" }] },
                },
                groups = new[] { new { name = "finance", path = "/finance" } },
                users = new[]
                {
                    new
                    {
                        username = Username,
                        email = Username,
                        emailVerified = true,
                        enabled = true,
                        realmRoles = new[] { "approver", $"default-roles-{realm.ToLowerInvariant()}" },
                        clientRoles = new Dictionary<string, string[]> { ["axis"] = ["reviewer"] },
                        groups = _finance,
                        credentials = new object[]
                        {
                            new { type = "password", value = password, temporary = false },
                            new
                            {
                                type = "otp",
                                secretData = $$"""{"value":"{{TotpSecret}}"}""",
                                credentialData = """{"subType":"totp","digits":6,"counter":0,"period":30,"algorithm":"HmacSHA1","secretEncoding":"BASE32"}""",
                            },
                        },
                    },
                },
            });
        Assert.True(imported.StatusCode == HttpStatusCode.OK, await imported.Content.ReadAsStringAsync(CancellationToken));

        var userId = (await GetAsync(realm, $"users?username={Uri.EscapeDataString(Username)}&exact=true")).EnumerateArray().Single().GetProperty("id").GetString()!;
        using (var client = await SendAsync(http, token, HttpMethod.Post, $"admin/realms/{realm}/clients", new { clientId = "site-gone" }))
        {
            Assert.Equal(HttpStatusCode.Created, client.StatusCode);
        }

        var uuid = (await GetAsync(realm, "clients?clientId=site-gone")).EnumerateArray().Single().GetProperty("id").GetString()!;
        using (var role = await SendAsync(http, token, HttpMethod.Post, $"admin/realms/{realm}/clients/{uuid}/roles", new { name = "gone" }))
        {
            Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        }

        var gone = await GetAsync(realm, $"clients/{uuid}/roles/gone");
        using var mapped = await SendAsync(http, token, HttpMethod.Post, $"admin/realms/{realm}/users/{userId}/role-mappings/clients/{uuid}", new[] { gone });
        Assert.Equal(HttpStatusCode.NoContent, mapped.StatusCode);
        return userId;
    }

    private async Task AddClientAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(
            http,
            await TokenAsync(http),
            HttpMethod.Post,
            $"admin/realms/{realm}/clients",
            new { clientId = SignInClient, publicClient = true, standardFlowEnabled = true, redirectUris = new[] { RedirectUri } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Signs in at the realm's own login form with the password and the authenticator code, as a browser would, and returns the sub claim of the ID token.
    private async Task<string> SignInAsync(string realm, string password)
    {
        // Keycloak marks its cookies Secure, which a CookieContainer does not send to http://localhost, so the test carries them itself.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(handler);
        var cookies = new Dictionary<string, string>();
        var pkce = Pkce.New();
        var authorize = new Uri(
            _baseAddress,
            $"realms/{realm}/protocol/openid-connect/auth?client_id={SignInClient}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&scope=openid&state=restored{pkce.Query}");
        var page = await SendWithCookiesAsync(browser, HttpMethod.Get, authorize, null, cookies);
        while (page.StatusCode == HttpStatusCode.Found)
        {
            page = await SendWithCookiesAsync(browser, HttpMethod.Get, page.Headers.Location!, null, cookies);
        }

        var html = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        var afterPassword = await SendWithCookiesAsync(browser, HttpMethod.Post, KeycloakSignIn.LoginAction(html), new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = password,
            ["credentialId"] = "",
        }, cookies);
        var codePage = await afterPassword.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(afterPassword.StatusCode == HttpStatusCode.OK && KeycloakSignIn.Page(codePage) == "twoStep", codePage);
        var login = await SendWithCookiesAsync(browser, HttpMethod.Post, KeycloakSignIn.LoginAction(codePage), new Dictionary<string, string>
        {
            ["otp"] = DevelopmentTotp.CodeFromSecret(TotpSecret),
            ["login"] = "Sign In",
        }, cookies);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.StartsWith(RedirectUri, callback.AbsoluteUri, StringComparison.Ordinal);
        var code = HttpUtility.ParseQueryString(callback.Query)["code"];
        Assert.False(string.IsNullOrEmpty(code), callback.Query);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = SignInClient,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = pkce.Verifier,
        });
        using var exchanged = await browser.PostAsync(new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), form, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        var tokens = await exchanged.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var payload = tokens.GetProperty("id_token").GetString()!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return claims.RootElement.GetProperty("sub").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpClient browser, HttpMethod method, Uri address, Dictionary<string, string>? form, Dictionary<string, string> cookies)
    {
        using var request = new HttpRequestMessage(method, address);
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        var response = await browser.SendAsync(request, CancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var pair in setCookies.Select(setCookie => setCookie.Split(';')[0].Split('=', 2)))
            {
                cookies[pair[0]] = pair[1];
            }
        }

        return response;
    }

    private Task<AdminResult> RestoreAsync(AdminScenario scenario, string file, string id, bool withClientSecret) =>
        scenario.RunAsync(
            AdminTenantRestoreTests.RestoreArgs(file, id, $"{id}.example.test"),
            withClientSecret: withClientSecret,
            path: _tools.PathSetting,
            extra: IdentityProvider,
            identityProvider: _handler);

    // Creates the tenant with its realm in the real Keycloak.
    private async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: IdentityProvider);
        Assert.Equal(0, created.Exit);
        return id;
    }

    private async Task DeleteRealmAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Delete, $"admin/realms/{realm}", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task CreateUserAsync(string realm, string username)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Post, $"admin/realms/{realm}/users", new { username, enabled = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<bool> RealmExistsAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}", null);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<string[]> ClientIdsAsync(string realm) =>
        [.. (await GetAsync(realm, "clients?max=1000")).EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString()!)
            .Where(clientId => clientId is "axis" or "axis-activation" or "axis-users" || clientId.StartsWith("site-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private async Task<string[]> ProviderAliasesAsync(string realm) =>
        [.. (await GetAsync(realm, "identity-provider/instances")).EnumerateArray()
            .Select(provider => provider.GetProperty("alias").GetString()!)
            .Where(alias => alias.StartsWith("oidc-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private async Task<string[]> UsernamesAsync(string realm, string username) =>
        [.. (await GetAsync(realm, $"users?username={username}&exact=true")).EnumerateArray().Select(user => user.GetProperty("username").GetString()!)];

    private async Task<JsonElement> GetAsync(string realm, string path)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        using var response = await SendAsync(http, await TokenAsync(http), HttpMethod.Get, $"admin/realms/{realm}/{path}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
    }

    private async Task<string> TokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }
}
