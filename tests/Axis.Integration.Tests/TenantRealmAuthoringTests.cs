using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Axis.Admin;
using Axis.Server.Tests;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The realm that <see cref="TenantRealm"/> makes in a real Keycloak lets an outside MCP client sign a person in and call <c>/mcp</c>
/// (<c>docs/reference/mcp-server.md#sign-in</c>): a public client gets a token for <c>https://localhost/mcp</c> through the code flow with
/// PKCE <c>S256</c> and the scope <c>axis:authoring</c>, the realm refuses an authorization request without the challenge, access tokens last 5 minutes,
/// a refresh token works once, and the <c>resource</c> parameter of RFC 8707 does not change the audience. An outside client registers itself at the realm
/// without a token (<c>docs/reference/mcp-server.md#client-registration</c>): it becomes a public client that requires consent, the consent screen names it,
/// and the realm refuses a registration of a confidential client, of the implicit or password flow, of an <c>http</c> redirect address that is not a loopback address
/// and of a scope other than <c>axis:authoring</c> and the realm's default scopes.
/// </summary>
public sealed class TenantRealmAuthoringTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string ClientId = "mcp-public-test";
    private const string RedirectUri = "http://localhost/callback";
    private const string LoopbackRedirectUri = "http://127.0.0.1/callback";
    private const string DraftHelper = "Draft helper";
    private const string Username = "pat";
    private const string Password = "a-long-enough-password";
    private const string Host = "localhost";
    private const string McpUrl = "https://localhost/mcp";
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    // The mapper stands in for the roles that tenant realms do not yet put into MCP tokens.
    private static readonly Dictionary<string, object> _rolesMapper = new()
    {
        ["name"] = "roles",
        ["protocol"] = "openid-connect",
        ["protocolMapper"] = "oidc-hardcoded-claim-mapper",
        ["config"] = new Dictionary<string, string>
        {
            ["claim.name"] = "roles",
            ["claim.value"] = "author",
            ["jsonType.label"] = "String",
            ["access.token.claim"] = "true",
            ["id.token.claim"] = "false",
            ["userinfo.token.claim"] = "false",
        },
    };

    private static readonly TenantRealmSecrets _secrets = new("platform-secret-1", "activation-secret-1", "users-secret-1");

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;
    private string _token = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        _token = await MasterTokenAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _http.Dispose();
    }

    [Fact]
    public async Task A_public_client_signs_a_person_in_with_pkce_and_the_scope_and_gets_a_session_from_initialize_on_mcp()
    {
        var (realm, secret) = await NewRealmAsync();
        var tokens = await SignInAsync(realm, secret);
        var response = await InitializeMcpAsync(realm, tokens.Tokens.GetProperty("access_token").GetString());

        Assert.True(response.Status == HttpStatusCode.OK, response.Body);
        Assert.False(string.IsNullOrEmpty(response.SessionId));
    }

    [Fact]
    public async Task A_client_registered_dynamically_is_public_requires_consent_and_opens_an_mcp_session()
    {
        var (realm, secret) = await NewRealmAsync();

        var (status, registered) = await RegisterAsync(realm, []);

        Assert.True(status == HttpStatusCode.Created, registered.GetRawText());
        var registeredId = registered.GetProperty("client_id").GetString()!;
        var client = (await GetJsonAsync($"admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(registeredId)}")).EnumerateArray().Single();
        Assert.True(client.GetProperty("publicClient").GetBoolean());
        Assert.True(client.GetProperty("consentRequired").GetBoolean());
        Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.False(client.GetProperty("serviceAccountsEnabled").GetBoolean());
        Assert.False(client.GetProperty("fullScopeAllowed").GetBoolean());
        using (var mapped = await SendAsync(
            HttpMethod.Post, $"admin/realms/{realm}/clients/{client.GetProperty("id").GetString()}/protocol-mappers/models", _rolesMapper))
        {
            Assert.Equal(HttpStatusCode.Created, mapped.StatusCode);
        }

        string? consentFor = null;
        var tokens = await SignInAsync(realm, secret, registeredId, LoopbackRedirectUri, onConsent: name => consentFor = name);

        Assert.Equal(DraftHelper, consentFor);
        var response = await InitializeMcpAsync(realm, tokens.Tokens.GetProperty("access_token").GetString());
        Assert.True(response.Status == HttpStatusCode.OK, response.Body);
        Assert.False(string.IsNullOrEmpty(response.SessionId));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8123/callback")]
    [InlineData("http://[::1]:8123/callback")]
    [InlineData("https://client.example/callback")]
    public async Task The_realm_accepts_a_dynamic_registration_with_an_https_or_a_loopback_redirect_address(string redirectUri)
    {
        var (realm, _) = await NewRealmAsync();

        var (status, registered) = await RegisterAsync(realm, new() { ["redirect_uris"] = new[] { redirectUri } });

        Assert.True(status == HttpStatusCode.Created, registered.GetRawText());
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "a confidential client", """{"token_endpoint_auth_method":"client_secret_basic"}""" },
        { "the implicit flow", """{"grant_types":["implicit"],"response_types":["token"]}""" },
        { "the password flow", """{"grant_types":["password"]}""" },
        { "an http redirect address that is not a loopback address", """{"redirect_uris":["http://client.example/callback"]}""" },
        { "localhost", """{"redirect_uris":["http://localhost:8123/callback"]}""" },
        { "a redirect address with a wildcard", """{"redirect_uris":["https://*.example/callback"]}""" },
        { "a scope other than the authoring scope", """{"scope":"phone"}""" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task The_realm_refuses_a_dynamic_registration_that_asks_for_more(string asks, string overrides)
    {
        var (realm, _) = await NewRealmAsync();
        var before = (await GetJsonAsync($"admin/realms/{realm}/clients")).GetArrayLength();
        using var document = JsonDocument.Parse(overrides);

        var (status, refusal) = await RegisterAsync(realm, document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value));

        Assert.True(status == HttpStatusCode.Forbidden, $"{asks}: {refusal.GetRawText()}");
        Assert.Equal(before, (await GetJsonAsync($"admin/realms/{realm}/clients")).GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task The_realm_refuses_the_same_asks_in_an_update_with_the_registration_access_token(string asks, string overrides)
    {
        var (realm, _) = await NewRealmAsync();
        var (status, registered) = await RegisterAsync(realm, []);
        Assert.True(status == HttpStatusCode.Created, registered.GetRawText());
        var registeredId = registered.GetProperty("client_id").GetString()!;
        var update = registered.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value);
        using var document = JsonDocument.Parse(overrides);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            update[property.Name] = property.Value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(_baseAddress, new Uri(registered.GetProperty("registration_client_uri").GetString()!).AbsolutePath))
        {
            Content = JsonContent.Create(update),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registered.GetProperty("registration_access_token").GetString());
        using var response = await _http.SendAsync(request, CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{asks}: {response.StatusCode} {await response.Content.ReadAsStringAsync(CancellationToken)}");
        var client = (await GetJsonAsync($"admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(registeredId)}")).EnumerateArray().Single();
        Assert.True(client.GetProperty("publicClient").GetBoolean());
        Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.Equal([LoopbackRedirectUri], client.GetProperty("redirectUris").EnumerateArray().Select(uri => uri.GetString()));
    }

    [Fact]
    public async Task A_rerun_repairs_the_registration_rules_that_were_changed_by_hand()
    {
        var (realm, _) = await NewRealmAsync();
        var components = $"admin/realms/{realm}/components?type={Uri.EscapeDataString(RealmSync.RegistrationPolicyType)}";
        var scopes = (await GetJsonAsync(components)).EnumerateArray()
            .Single(component => component.GetProperty("subType").GetString() == "anonymous" && component.GetProperty("providerId").GetString() == "allowed-client-templates");
        var widened = scopes.Deserialize<Dictionary<string, JsonElement>>()!;
        widened["config"] = JsonSerializer.SerializeToElement(new Dictionary<string, string[]>
        {
            ["allowed-client-scopes"] = [RealmSync.AuthoringScope, "phone"],
            ["allow-default-scopes"] = ["true"],
        });
        using (var put = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}/components/{scopes.GetProperty("id").GetString()}", widened))
        {
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        var (widenedStatus, _) = await RegisterAsync(realm, new() { ["scope"] = "phone" });
        Assert.Equal(HttpStatusCode.Created, widenedStatus);

        var admin = new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        await new TenantRealm(_http, admin).EnsureAsync(realm, "Acme Corporation", _secrets, new Dictionary<string, string>(), CancellationToken);
        await new RealmSync(_http, _baseAddress).SyncPlatformAddressesAsync(realm, _secrets.Activation, [Host], CancellationToken);

        var (repairedStatus, _) = await RegisterAsync(realm, new() { ["scope"] = "phone" });
        Assert.Equal(HttpStatusCode.Forbidden, repairedStatus);
        var (allowedStatus, _) = await RegisterAsync(realm, []);
        Assert.Equal(HttpStatusCode.Created, allowedStatus);
        Assert.Equal(1, (await GetJsonAsync(components)).EnumerateArray().Count(component =>
            component.GetProperty("subType").GetString() == "anonymous" && component.GetProperty("providerId").GetString() == "allowed-client-templates"));
    }

    [Fact]
    public async Task The_realm_refuses_an_authorization_request_from_a_public_client_that_has_no_pkce_challenge()
    {
        var (realm, _) = await NewRealmAsync();
        var pkce = Pkce.New();

        foreach (var query in new[]
        {
            string.Empty,
            "&code_challenge_method=S256",
            $"&code_challenge={pkce.Challenge}&code_challenge_method=plain",
        })
        {
            using var response = await _http.GetAsync(new Uri(_baseAddress, AuthorizePath(realm) + query), CancellationToken);

            // Keycloak sends the error to the client's redirect address, so no code is issued and no sign-in page is shown.
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var location = response.Headers.Location!;
            Assert.StartsWith(RedirectUri, location.AbsoluteUri, StringComparison.Ordinal);
            var values = HttpUtility.ParseQueryString(location.Query);
            Assert.Null(values["code"]);
            Assert.Equal("invalid_request", values["error"]);
        }
    }

    [Fact]
    public async Task A_refresh_token_works_once_and_the_access_token_is_for_the_mcp_url_and_lasts_five_minutes()
    {
        var (realm, secret) = await NewRealmAsync();
        var tokens = (await SignInAsync(realm, secret)).Tokens;
        var first = tokens.GetProperty("refresh_token").GetString()!;
        var access = tokens.GetProperty("access_token").GetString()!;
        // Keycloak counts the time left when it builds the response, so a second may have passed since the token was issued.
        Assert.InRange(tokens.GetProperty("expires_in").GetInt32(), TenantRealm.AccessTokenLifespanSeconds - 1, TenantRealm.AccessTokenLifespanSeconds);
        using (var claims = Claims(access))
        {
            Assert.Equal(TenantRealm.AccessTokenLifespanSeconds, claims.RootElement.GetProperty("exp").GetInt64() - claims.RootElement.GetProperty("iat").GetInt64());
        }
        Assert.Contains(McpUrl, Audiences(access));

        var refreshed = await RefreshAsync(realm, first);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        var second = refreshed.Body.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Contains(McpUrl, Audiences(refreshed.Body.GetProperty("access_token").GetString()!));

        var reused = await RefreshAsync(realm, first);
        Assert.Equal(HttpStatusCode.BadRequest, reused.Status);
        Assert.Equal("invalid_grant", reused.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_resource_parameter_does_not_change_the_audience_of_the_token()
    {
        var (realm, secret) = await NewRealmAsync();

        var tokens = (await SignInAsync(realm, secret, resource: "https://elsewhere.example/mcp")).Tokens;

        var audiences = Audiences(tokens.GetProperty("access_token").GetString()!);
        Assert.Contains(McpUrl, audiences);
        Assert.DoesNotContain("https://elsewhere.example/mcp", audiences);
        Assert.Equal(TenantRealm.AccessTokenLifespanSeconds, (await GetJsonAsync($"admin/realms/{realm}")).GetProperty("accessTokenLifespan").GetInt32());
        var optional = (await GetJsonAsync($"admin/realms/{realm}/default-optional-client-scopes")).EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString()).ToList();
        Assert.Contains(RealmSync.AuthoringScope, optional);
        Assert.DoesNotContain("offline_access", optional);
    }

    [Fact]
    public async Task Each_host_write_gives_the_authoring_scope_its_consent_text_even_when_the_text_was_removed()
    {
        var (realm, _) = await NewRealmAsync();

        var scope = await AuthoringScopeAsync(realm);
        Assert.Equal("${axisAuthoringScopeConsentText}", Attribute(scope, "consent.screen.text"));
        Assert.Equal("true", Attribute(scope, "display.on.consent.screen"));

        var stripped = scope.Deserialize<Dictionary<string, JsonElement>>()!;
        var attributes = scope.GetProperty("attributes").Deserialize<Dictionary<string, JsonElement>>()!;
        // Keycloak keeps an attribute that an update leaves out, so the text is emptied.
        attributes["consent.screen.text"] = JsonSerializer.SerializeToElement(string.Empty);
        stripped["attributes"] = JsonSerializer.SerializeToElement(attributes);
        using (var removed = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}/client-scopes/{scope.GetProperty("id").GetString()}", stripped))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.NotEqual("${axisAuthoringScopeConsentText}", Attribute(await AuthoringScopeAsync(realm), "consent.screen.text"));

        await new RealmSync(_http, _baseAddress).SyncPlatformAddressesAsync(realm, _secrets.Activation, [Host], CancellationToken);

        var repaired = await AuthoringScopeAsync(realm);
        Assert.Equal("${axisAuthoringScopeConsentText}", Attribute(repaired, "consent.screen.text"));
        Assert.Equal("true", Attribute(repaired, "include.in.token.scope"));
    }

    private async Task<JsonElement> AuthoringScopeAsync(string realm) =>
        (await GetJsonAsync($"admin/realms/{realm}/client-scopes")).EnumerateArray()
            .Single(scope => scope.GetProperty("name").GetString() == RealmSync.AuthoringScope);

    private static string? Attribute(JsonElement scope, string name) =>
        scope.TryGetProperty("attributes", out var attributes) && attributes.TryGetProperty(name, out var value) ? value.GetString() : null;

    // One realm for each test, because Keycloak refuses an authenticator code that was already used in its period.
    private async Task<(string Realm, string Secret)> NewRealmAsync()
    {
        var realm = $"t{Guid.NewGuid():N}"[..16];
        _realms.Add(realm);
        var admin = new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        await new TenantRealm(_http, admin).EnsureAsync(realm, "Acme Corporation", _secrets, new Dictionary<string, string>(), CancellationToken);
        await new RealmSync(_http, _baseAddress).SyncPlatformAddressesAsync(realm, _secrets.Activation, [Host], CancellationToken);

        // The client names no scope, so the test shows that the realm's default makes the scope available.
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", new
        {
            clientId = ClientId,
            publicClient = true,
            standardFlowEnabled = true,
            redirectUris = new[] { RedirectUri },
            protocolMappers = new[] { _rolesMapper },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        const string secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
        using var user = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", new
        {
            username = Username,
            enabled = true,
            emailVerified = true,
            email = $"{Username}@example.test",
            firstName = "Pat",
            lastName = "Example",
            credentials = new object[]
            {
                new { type = "password", value = Password, temporary = false },
                new
                {
                    type = "otp",
                    secretData = $$"""{"value":"{{secret}}"}""",
                    credentialData = """{"subType":"totp","digits":6,"counter":0,"period":30,"algorithm":"HmacSHA1","secretEncoding":"BASE32"}""",
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return (realm, secret);
    }

    private static string AuthorizePath(string realm, string clientId = ClientId, string redirectUri = RedirectUri) =>
        $"realms/{realm}/protocol/openid-connect/auth?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString($"openid {RealmSync.AuthoringScope}")}";

    /// <summary>
    /// Signs the person in at the realm as a browser does, with the code flow and PKCE <c>S256</c>, and returns the token response. A client that
    /// requires consent gets the consent page, which the person allows. <paramref name="onConsent"/> sees the client name that the page shows.
    /// </summary>
    private async Task<(JsonElement Tokens, string? ConsentFor)> SignInAsync(
        string realm, string secret, string clientId = ClientId, string redirectUri = RedirectUri, string? resource = null, Action<string>? onConsent = null)
    {
        var pkce = Pkce.New();
        var query = pkce.Query;
        if (resource is not null)
        {
            query += $"&resource={Uri.EscapeDataString(resource)}";
        }

        var authorize = new Uri(_baseAddress, AuthorizePath(realm, clientId, redirectUri) + query);
        var (password, cookies) = await KeycloakSignIn.SubmitPasswordAsync(_http, authorize, Username, Password);
        var passwordCookies = WithCookies(cookies, password);
        var login = await KeycloakSignIn.SubmitCodeAsync(_http, password, cookies, Username, DevelopmentTotp.CodeFromSecret(secret));
        string? consentFor = null;
        var cookieHeader = WithCookies(passwordCookies, login);
        // Keycloak sends the browser on to the consent step at its own address.
        if (login.StatusCode == HttpStatusCode.Found && login.Headers.Location!.AbsoluteUri.StartsWith(_baseAddress.AbsoluteUri, StringComparison.Ordinal))
        {
            using var step = new HttpRequestMessage(HttpMethod.Get, login.Headers.Location);
            step.Headers.Add("Cookie", cookieHeader);
            login = await _http.SendAsync(step, CancellationToken);
            cookieHeader = WithCookies(cookieHeader, login);
        }

        if (login.StatusCode == HttpStatusCode.OK)
        {
            var html = await login.Content.ReadAsStringAsync(CancellationToken);
            Assert.Equal("consent", KeycloakSignIn.Page(html));
            consentFor = KeycloakSignIn.Property(html, "clientName")!;
            onConsent?.Invoke(consentFor);
            using var accepted = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, KeycloakSignIn.Property(html, "oauthAction")))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = KeycloakSignIn.Property(html, "code")!,
                    ["accept"] = "yes",
                }),
            };
            accepted.Headers.Add("Cookie", cookieHeader);
            login = await _http.SendAsync(accepted, CancellationToken);
        }

        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var location = login.Headers.Location!;
        Assert.StartsWith(redirectUri, location.AbsoluteUri, StringComparison.Ordinal);
        var code = HttpUtility.ParseQueryString(location.Query)["code"];
        Assert.False(string.IsNullOrEmpty(code), location.AbsoluteUri);

        var response = await TokenAsync(realm, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = pkce.Verifier,
        });
        Assert.True(response.Status == HttpStatusCode.OK, response.Body.GetRawText());
        return (response.Body, consentFor);
    }

    // The Cookie header that the browser sends after a response: a cookie that the response sets again replaces the one the browser held.
    private static string WithCookies(string cookieHeader, HttpResponseMessage response)
    {
        var cookies = cookieHeader.Split("; ").ToDictionary(cookie => cookie.Split('=', 2)[0], cookie => cookie);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies.Select(setCookie => setCookie.Split(';')[0]))
            {
                cookies[cookie.Split('=', 2)[0]] = cookie;
            }
        }

        return string.Join("; ", cookies.Values);
    }

    /// <summary>Registers a client at the realm without a token, as an outside MCP client does. The <paramref name="overrides"/> replace the fields of a valid registration.</summary>
    private async Task<(HttpStatusCode Status, JsonElement Body)> RegisterAsync(string realm, Dictionary<string, object?> overrides)
    {
        var registration = new Dictionary<string, object?>
        {
            ["client_name"] = DraftHelper,
            ["redirect_uris"] = new[] { LoopbackRedirectUri },
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
            ["scope"] = RealmSync.AuthoringScope,
        };
        foreach (var (name, value) in overrides)
        {
            registration[name] = value;
        }

        using var response = await _http.PostAsJsonAsync(
            new Uri(_baseAddress, $"realms/{realm}/clients-registrations/openid-connect"), registration, CancellationToken);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken));
    }

    private async Task<McpInitialize> InitializeMcpAsync(string realm, string? accessToken)
    {
        var applications = Directory.CreateTempSubdirectory("axis-mcp-realm-").FullName;
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseTestPlatform(UnreachableDatabase);
                builder.WithoutPlatformMigration();
                builder.UseSetting("Tenants:a:Hosts:0", Host);
                builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
                builder.UseSetting("Tenants:a:SchemaConnectionString", UnreachableDatabase);
                builder.UseSetting("Tenants:a:Oidc:Authority", new Uri(_baseAddress, $"realms/{realm}").ToString());
                builder.UseSetting("Tenants:a:Oidc:ClientId", TenantRealm.PlatformClientId);
                builder.UseSetting("Tenants:a:Oidc:ClientSecret", _secrets.Platform);
                builder.UseSetting("Studio:AuthorRole", "author");
                builder.UseSetting("Studio:ApplicationsPath", applications);
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false,
            });

            using var response = await McpTestCalls.PostAsync(client, Host, accessToken, McpTestCalls.Initialize("2025-11-25"), null, null, null, CancellationToken);
            return new McpInitialize(
                response.StatusCode,
                await response.Content.ReadAsStringAsync(CancellationToken),
                response.Headers.TryGetValues("Mcp-Session-Id", out var sessions) ? Assert.Single(sessions) : null);
        }
        finally
        {
            Directory.Delete(applications, recursive: true);
        }
    }

    private sealed record McpInitialize(HttpStatusCode Status, string Body, string? SessionId);

    private Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(string realm, string refreshToken) =>
        TokenAsync(realm, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
        });

    private async Task<(HttpStatusCode Status, JsonElement Body)> TokenAsync(string realm, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken));
    }

    // The claims of an access token, read from its payload without checking the signature, which the MCP server checks.
    private static JsonDocument Claims(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload));
    }

    private static List<string> Audiences(string accessToken)
    {
        using var claims = Claims(accessToken);
        var audience = claims.RootElement.GetProperty("aud");
        return audience.ValueKind == JsonValueKind.Array
            ? [.. audience.EnumerateArray().Select(item => item.GetString()!)]
            : [audience.GetString()!];
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
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
}
