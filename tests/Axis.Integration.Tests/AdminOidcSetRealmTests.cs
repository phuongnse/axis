using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Realms;
using Axis.Configuration.Tests;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant oidc set</c> on a real Keycloak updates the tenant's own realm. A client key and the profile reach the
/// platform client <c>axis</c> and every <c>site-</c> client, activation keeps the site clients in step with <c>axis</c>, and
/// a <c>fapi2</c> tenant signs in through its realm with PAR, <c>private_key_jwt</c> and DPoP. A realm that cannot be updated
/// leaves the tenant as it was.
/// </summary>
public sealed class AdminOidcSetRealmTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string TokenPath = "/protocol/openid-connect/token";
    private const string ParPath = "/protocol/openid-connect/ext/par/request";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(http, token, HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task A_client_key_and_the_profile_reach_the_axis_client_and_the_site_clients_and_activation_keeps_them()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);
        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);
        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await MasterTokenAsync(http);
        await SyncAsync(http, scenario, id, ["a"]);
        var first = NewKeyPem();
        var second = NewKeyPem();
        var firstId = TenantClientKeysId(first);
        var secondId = TenantClientKeysId(second);

        var added = await SetAsync(scenario, id, first, "--client-key", "--profile", "fapi2");
        Assert.Equal(0, added.Exit);
        foreach (var clientId in new[] { "axis", "site-a" })
        {
            var client = await ClientAsync(http, admin, id, clientId);
            Assert.Equal("client-jwt", client.GetProperty("clientAuthenticatorType").GetString());
            Assert.Equal("true", Attribute(client, "require.pushed.authorization.requests"));
            Assert.Equal("true", Attribute(client, "dpop.bound.access.tokens"));
            Assert.Equal("S256", Attribute(client, "pkce.code.challenge.method"));
            Assert.Equal("ES256", Attribute(client, "token.endpoint.auth.signing.alg"));
            Assert.Equal([firstId], Kids(client));
        }

        Assert.Equal(0, (await SetAsync(scenario, id, second, "--client-key")).Exit);
        foreach (var clientId in new[] { "axis", "site-a" })
        {
            Assert.Equal([secondId, firstId], Kids(await ClientAsync(http, admin, id, clientId)));
        }

        Assert.Equal(0, (await SetAsync(scenario, id, null, "--retire-client-key", firstId)).Exit);
        foreach (var clientId in new[] { "axis", "site-a" })
        {
            Assert.Equal([secondId], Kids(await ClientAsync(http, admin, id, clientId)));
        }

        // Activation runs again: the site client that exists keeps the keys and the profile, and the new one gets them.
        await SyncAsync(http, scenario, id, ["a", "b"]);
        foreach (var clientId in new[] { "site-a", "site-b" })
        {
            var client = await ClientAsync(http, admin, id, clientId);
            Assert.Equal("client-jwt", client.GetProperty("clientAuthenticatorType").GetString());
            Assert.Equal("true", Attribute(client, "require.pushed.authorization.requests"));
            Assert.Equal("true", Attribute(client, "dpop.bound.access.tokens"));
            Assert.Equal("S256", Attribute(client, "pkce.code.challenge.method"));
            Assert.Equal([secondId], Kids(client));
        }

        var standard = await SetAsync(scenario, id, null, "--profile", "standard", "--retire-client-key", secondId);
        Assert.Equal(0, standard.Exit);
        foreach (var clientId in new[] { "axis", "site-a", "site-b" })
        {
            var client = await ClientAsync(http, admin, id, clientId);
            Assert.Equal("client-secret", client.GetProperty("clientAuthenticatorType").GetString());
            Assert.Equal("false", Attribute(client, "require.pushed.authorization.requests"));
            Assert.NotEqual("true", Attribute(client, "dpop.bound.access.tokens"));
            Assert.True(string.IsNullOrEmpty(Attribute(client, "pkce.code.challenge.method")));
            Assert.Empty(Kids(client));
        }

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_realm_that_cannot_be_updated_fails_the_command_and_leaves_the_tenant_as_it_was()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);
        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);
        var before = await scenario.RowAsync(id);

        var result = await scenario.RunAsync(
            ["tenant", "oidc", "set", id, "--client-key", "--profile", "fapi2"],
            extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory", [Axis.Admin.AdminCommands.ClientKeySetting] = NewKeyPem() },
            identityProvider: new FailingHandler());

        Assert.Equal(1, result.Exit);
        var line = Assert.Single(result.Output);
        Assert.StartsWith($"{id} realm failed: ", line, StringComparison.Ordinal);
        var after = await scenario.RowAsync(id);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.OidcProfile, after.OidcProfile);
        Assert.Equal(before.OidcClientKeyId, after.OidcClientKeyId);
        Assert.Equal(before.OidcPreviousClientKeyId, after.OidcPreviousClientKeyId);
        Assert.Equal(before.OidcClientKey, after.OidcClientKey);
    }

    [Fact]
    public async Task A_fapi2_tenant_signs_in_through_its_own_realm_with_par_a_client_assertion_and_dpop()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = NewTenantId(scenario);
        Assert.Equal(0, (await CreateAsync(scenario, id)).Exit);
        var host = $"{id}.example.test";
        using var http = new HttpClient(_handler, disposeHandler: false);
        var admin = await MasterTokenAsync(http);

        // Host commands do not update the realm's redirect addresses yet, so the test adds the one it signs in at.
        var axis = await ClientAsync(http, admin, id, "axis");
        var representation = JsonNode.Parse(axis.GetRawText())!.AsObject();
        representation["redirectUris"] = new JsonArray($"http://{host}/*");
        using (var updated = await SendAsync(http, admin, HttpMethod.Put, $"admin/realms/{id}/clients/{axis.GetProperty("id").GetString()}", representation))
        {
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        }

        var password = Guid.NewGuid().ToString("N");
        var secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
        using (var created = await SendAsync(http, admin, HttpMethod.Post, $"admin/realms/{id}/users", new
        {
            username = "pat",
            enabled = true,
            emailVerified = true,
            email = "pat@example.test",
            firstName = "Pat",
            lastName = "Example",
            credentials = new object[]
            {
                new { type = "password", value = password, temporary = false },
                new
                {
                    type = "otp",
                    secretData = $$"""{"value":"{{secret}}"}""",
                    credentialData = """{"subType":"totp","digits":6,"period":30,"algorithm":"HmacSHA1","counter":0,"secretEncoding":"BASE32"}""",
                },
            },
        }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var found = await SendAsync(http, admin, HttpMethod.Get, $"admin/realms/{id}/users?username=pat&exact=true");
        var users = await found.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var userId = users[0].GetProperty("id").GetString()!;

        Assert.Equal(0, (await SetAsync(scenario, id, NewKeyPem(), "--profile", "fapi2", "--client-key")).Exit);
        var recorder = new RecordingHandler();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(scenario.Platform);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);

            // An hour, so the timer never races the sign-in.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureTestServices(services =>
                services.Configure<OpenIdConnectOptions>(TenantSignIn.SchemeName(id), options => options.BackchannelHttpHandler = recorder));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{host}"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var callback = await LogInAtRealmAsync(client, id, "pat", password, secret);
        using var signedIn = await client.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        using var me = await client.GetAsync("/api/me", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(userId, (await me.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString());

        var par = Assert.Single(recorder.Calls, call => call.Url.EndsWith(ParPath, StringComparison.Ordinal));
        Assert.NotEmpty(par.Form["client_assertion"]);
        Assert.Equal("S256", par.Form["code_challenge_method"]);
        var token = Assert.Single(recorder.Calls, call => call.Url.EndsWith(TokenPath, StringComparison.Ordinal));
        Assert.NotEmpty(token.Form["client_assertion"]);
        Assert.NotNull(token.Dpop);
        using var tokenResponse = JsonDocument.Parse(token.ResponseBody!);
        Assert.Equal("DPoP", tokenResponse.RootElement.GetProperty("token_type").GetString());
        Assert.All(recorder.Calls.Where(call => call.Form.Count > 0), call => Assert.DoesNotContain("client_secret", call.Form.Keys));
    }

    private string NewTenantId(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        return id;
    }

    private static string NewKeyPem() => ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();

    private static string TenantClientKeysId(string pem)
    {
        Assert.True(Axis.Administration.TenantClientKeys.TryRead(pem, out _, out var keyId, out _));
        return keyId;
    }

    // Runs against the real Keycloak, with its admin client and the real HTTP handler in place of the stub.
    private Task<AdminResult> CreateAsync(AdminScenario scenario, string id) =>
        scenario.RunAsync(
            AdminScenario.CreateArgs(id, $"{id}.example.test"),
            identityProvider: _handler,
            extra: new Dictionary<string, string?>
            {
                ["IdentityProvider:Url"] = _baseAddress.ToString(),
                ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
                ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
            });

    // The key goes in the environment setting, never in an argument.
    private Task<AdminResult> SetAsync(AdminScenario scenario, string id, string? pem, params string[] options) =>
        scenario.RunAsync(
            ["tenant", "oidc", "set", id, .. options],
            extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory", [Axis.Admin.AdminCommands.ClientKeySetting] = pem },
            identityProvider: _handler);

    // Brings the realm's site clients to the given paths, as activation does.
    private async Task SyncAsync(HttpClient http, AdminScenario scenario, string id, string[] paths)
    {
        var row = await scenario.RowAsync(id);
        var activation = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.RealmActivationClientSecret, row.RealmActivationClientSecret!).Reveal();
        await new RealmSync(http, _baseAddress).SyncAsync(id, activation, new RealmState([.. paths.Select(path => new RealmSiteClient(path, Accounts: true, []))], []), CancellationToken);
    }

    private async Task<JsonElement> ClientAsync(HttpClient http, string admin, string realm, string clientId)
    {
        using var response = await SendAsync(http, admin, HttpMethod.Get, $"admin/realms/{realm}/clients?clientId={clientId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clients = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return Assert.Single(clients.EnumerateArray()).Clone();
    }

    private static string? Attribute(JsonElement client, string name) =>
        client.GetProperty("attributes").TryGetProperty(name, out var value) ? value.GetString() : null;

    // The key ids in the client's JWKS, in the order the realm holds them.
    private static string[] Kids(JsonElement client)
    {
        var jwks = Attribute(client, "jwks.string");
        if (jwks is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(jwks);
        return [.. document.RootElement.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString()!)];
    }

    private async Task<string> MasterTokenAsync(HttpClient http)
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

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, CancellationToken);
    }

    /// <summary>
    /// Starts sign-in on the server and logs in at the tenant realm's own login form, which uses the Axis theme.
    /// Returns the callback address that the realm sends the browser to, which has not been called.
    /// </summary>
    private static async Task<Uri> LogInAtRealmAsync(HttpClient client, string realm, string username, string password, string otpSecret)
    {
        using var challenge = await client.GetAsync("/api/auth/sign-in", CancellationToken);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.Contains($"/realms/{realm}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("request_uri=", authorize.Query, StringComparison.Ordinal);

        // A separate client plays the browser. Keycloak marks its cookies Secure, so the client carries them itself.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var cookies = new Dictionary<string, string>();
        var page = await GetAsync(browser, authorize, cookies);
        while (page.StatusCode == HttpStatusCode.Found)
        {
            page = await GetAsync(browser, page.Headers.Location!, cookies);
        }

        var html = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        var afterPassword = await PostAsync(browser, KeycloakSignIn.LoginAction(html).AbsoluteUri, cookies, new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["credentialId"] = "",
        });
        var codePage = await afterPassword.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(afterPassword.StatusCode == HttpStatusCode.OK && KeycloakSignIn.Page(codePage) == "twoStep", codePage);
        var login = await PostAsync(browser, KeycloakSignIn.LoginAction(codePage).AbsoluteUri, cookies, new Dictionary<string, string>
        {
            ["otp"] = DevelopmentTotp.CodeFromSecret(otpSecret),
            ["login"] = "Sign In",
        });
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal($"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        return callback;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient browser, Uri address, Dictionary<string, string> cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        return await SendWithCookiesAsync(browser, request, cookies);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string action, Dictionary<string, string> cookies, Dictionary<string, string> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(action)) { Content = new FormUrlEncodedContent(form) };
        return await SendWithCookiesAsync(browser, request, cookies);
    }

    private static async Task<HttpResponseMessage> SendWithCookiesAsync(HttpClient browser, HttpRequestMessage request, Dictionary<string, string> cookies)
    {
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

    /// <summary>Answers every request with a server error, as an identity provider that is down would.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    /// <summary>One request that the server sent to the realm: its address, form fields, <c>DPoP</c> header and the body of the answer.</summary>
    private sealed record RecordedCall(string Url, Dictionary<string, string> Form, string? Dpop, string? ResponseBody);

    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly ConcurrentQueue<RecordedCall> _calls = new();

        public RecordingHandler()
            : base(new HttpClientHandler())
        {
        }

        public IReadOnlyCollection<RecordedCall> Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = new Dictionary<string, string>();
            if (request.Content is { } content && content.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded")
            {
                foreach (var pair in (await content.ReadAsStringAsync(cancellationToken)).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    form[WebUtility.UrlDecode(parts[0])] = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : "";
                }
            }

            var dpop = request.Headers.TryGetValues("DPoP", out var proofs) ? proofs.Single() : null;
            var response = await base.SendAsync(request, cancellationToken);
            string? body = null;
            if (request.RequestUri!.AbsolutePath.EndsWith(TokenPath, StringComparison.Ordinal))
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            }

            _calls.Enqueue(new RecordedCall(request.RequestUri.ToString(), form, dpop, body));
            return response;
        }
    }
}
