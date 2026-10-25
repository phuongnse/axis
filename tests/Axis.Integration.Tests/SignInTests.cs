using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in through the development realm in Keycloak, with the code flow and PKCE, and the cookie
/// session it leaves. Tenant a is served at localhost and tenant b at b.example.test, and both sign
/// in at the same realm. The test drives Keycloak's login form as a browser would. Each tenant has a
/// migrated database, where sign-in and sign-out write their audit records. The server sits behind
/// a listed proxy, so every request carries the client address 203.0.113.7.
/// </summary>
public sealed partial class SignInTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Proxy = "10.0.0.5";
    private const string ClientAddress = "203.0.113.7";
    private const string UserAgent = "AxisAuditProbe/1.0";
    private const string HostB = "b.example.test";
    private const string AuditSelect = "SELECT actor, actor_name, action, application_id, details::text FROM axis.audit_records";

    // The realm's client accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private readonly List<string> _serverResponses = [];
    private readonly Dictionary<string, string> _databases = [];
    private Uri? _authority;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Uri Authority => _authority ?? throw new InvalidOperationException("The test host is not started.");

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task Sign_in_uses_pkce_and_the_session_returns_the_user_from_the_claims()
    {
        using var client = CreateClient();

        var signIn = await SignInAsync(client, "anna", "/apps/purchase-requests");
        var me = await SendAsync(client, HttpMethod.Get, "/api/me");

        Assert.Equal("/apps/purchase-requests", signIn.ReturnedTo);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("anna", body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Anna Employee", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(["employee", "requester"], body.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sign_in_without_a_return_address_returns_to_the_root()
    {
        using var client = CreateClient();

        var signIn = await SignInAsync(client, "binh", returnUrl: null);

        Assert.Equal("/", signIn.ReturnedTo);
    }

    [Fact]
    public async Task The_cookie_holds_only_the_session_id_the_row_holds_only_its_hash_and_no_response_carries_a_token()
    {
        using var client = CreateClient();

        var signIn = await SignInAsync(client, "anna", "/");
        await SendAsync(client, HttpMethod.Get, "/api/me");

        Assert.NotEmpty(_serverResponses);
        foreach (var response in _serverResponses)
        {
            Assert.DoesNotMatch(JwtPattern(), response);
            Assert.DoesNotContain("access_token", response, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refresh_token", response, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("id_token", response, StringComparison.OrdinalIgnoreCase);
        }

        var sessionId = ReadSessionId(signIn.SessionCookie);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", sessionId);
        var idBytes = Base64Url.DecodeFromChars(sessionId);
        var row = Assert.Single(await ReadSessionsAsync("a"));
        Assert.Equal(SHA256.HashData(idBytes), row.IdHash);
        Assert.NotEqual(idBytes, row.IdHash);
        Assert.NotEqual(signIn.SessionCookie, Base64Url.EncodeToString(row.IdHash));
        Assert.NotEqual(sessionId, Base64Url.EncodeToString(row.IdHash));
        Assert.Equal("anna", row.UserId);
        Assert.Equal("Anna Employee", row.DisplayName);
        Assert.Equal(["employee", "requester"], row.Roles.Order(StringComparer.Ordinal));
        Assert.Empty(await ReadSessionsAsync("b"));
    }

    [Fact]
    public async Task A_copied_cookie_stops_working_once_the_session_signs_out()
    {
        using var client = CreateClient();
        var signIn = await SignInAsync(client, "anna", "/");
        using var cookieless = CreateCookielessClient();
        var copied = signIn.SessionCookie;
        Assert.Equal(HttpStatusCode.OK, (await GetMeWithCookieAsync(cookieless, "localhost", copied)).StatusCode);

        var signOut = await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out");

        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
        var afterSignOut = await GetMeWithCookieAsync(cookieless, "localhost", copied);
        Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
        Assert.Empty(await ReadSessionsAsync("a"));
    }

    [Fact]
    public async Task Signing_in_again_creates_a_new_session_id_and_ends_the_old_session()
    {
        using var client = CreateClient();

        var first = await SignInAsync(client, "anna", "/");
        var second = await SignInAsync(client, "anna", "/");

        var firstId = ReadSessionId(first.SessionCookie);
        var secondId = ReadSessionId(second.SessionCookie);
        Assert.NotEqual(firstId, secondId);
        var row = Assert.Single(await ReadSessionsAsync("a"));
        Assert.Equal(SHA256.HashData(Base64Url.DecodeFromChars(secondId)), row.IdHash);
        using var cookieless = CreateCookielessClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeWithCookieAsync(cookieless, "localhost", first.SessionCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetMeWithCookieAsync(cookieless, "localhost", second.SessionCookie)).StatusCode);
    }

    [Fact]
    public async Task A_session_taken_to_another_tenants_host_signs_nobody_in()
    {
        using var client = CreateClient();
        var signIn = await SignInAsync(client, "anna", "/");
        using var cookieless = CreateCookielessClient();

        var sameTenant = await GetMeWithCookieAsync(cookieless, "localhost", signIn.SessionCookie);
        var otherTenant = await GetMeWithCookieAsync(cookieless, HostB, signIn.SessionCookie);

        Assert.Equal(HttpStatusCode.OK, sameTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherTenant.StatusCode);
        Assert.Equal("application/problem+json", otherTenant.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_and_answers_the_identity_providers_sign_out_address()
    {
        using var client = CreateClient();
        await SignInAsync(client, "anna", "/");

        var signOut = await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out");
        var me = await SendAsync(client, HttpMethod.Get, "/api/me");

        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
        using var body = JsonDocument.Parse(await signOut.Content.ReadAsStringAsync(CancellationToken));
        var signOutUrl = body.RootElement.GetProperty("signOutUrl").GetString();
        Assert.StartsWith($"{Authority}/protocol/openid-connect/logout?", signOutUrl, StringComparison.Ordinal);
        Assert.Contains("client_id=axis", signOutUrl, StringComparison.Ordinal);
        Assert.Contains($"post_logout_redirect_uri={Uri.EscapeDataString("http://localhost:5206/")}", signOutUrl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Sign_out_without_the_csrf_header_is_refused_and_keeps_the_session()
    {
        using var client = CreateClient();
        await SignInAsync(client, "anna", "/");

        var signOut = await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out", csrfHeader: false);
        var me = await SendAsync(client, HttpMethod.Get, "/api/me");

        Assert.Equal(HttpStatusCode.Forbidden, signOut.StatusCode);
        Assert.Equal("application/problem+json", signOut.Content.Headers.ContentType?.MediaType);
        Assert.False(signOut.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Sign_in_writes_one_audit_record_with_the_user_and_the_client_address()
    {
        using var client = CreateClient();

        await SignInAsync(client, "anna", "/");

        var record = Assert.Single(await ReadAuditAsync("a", "session.signedIn"));
        Assert.Equal("anna", record.Actor);
        Assert.Equal("Anna Employee", record.ActorName);
        Assert.Null(record.ApplicationId);
        using var details = JsonDocument.Parse(record.Details);
        Assert.Equal(ClientAddress, details.RootElement.GetProperty("clientAddress").GetString());
        Assert.False(details.RootElement.TryGetProperty("reason", out _));
        Assert.Empty(await ReadAuditAsync("b"));
    }

    [Fact]
    public async Task Sign_out_writes_one_audit_record_and_a_refused_sign_out_writes_none()
    {
        using var client = CreateClient();
        await SignInAsync(client, "anna", "/");

        var refused = await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out", csrfHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(await ReadAuditAsync("a", "session.signedOut"));

        var signOut = await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out");

        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
        var record = Assert.Single(await ReadAuditAsync("a", "session.signedOut"));
        Assert.Equal("anna", record.Actor);
        Assert.Equal("Anna Employee", record.ActorName);
        using var details = JsonDocument.Parse(record.Details);
        Assert.Equal(ClientAddress, details.RootElement.GetProperty("clientAddress").GetString());
    }

    [Fact]
    public async Task Sign_out_without_a_session_writes_no_audit_record()
    {
        using var client = CreateClient();

        var signOut = await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out");

        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
        Assert.Empty(await ReadAuditAsync("a"));
    }

    [Fact]
    public async Task A_callback_with_the_state_of_another_tenants_sign_in_is_refused_and_recorded_in_the_tenant_of_its_host()
    {
        using var client = CreateClient();
        var challenge = await SendAsync(client, HttpMethod.Get, "/api/auth/sign-in");
        var state = Regex.Match(challenge.Headers.Location!.Query, "[?&]state=(?<state>[^&]+)").Groups["state"].Value;
        Assert.NotEmpty(state);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/auth/callback?code=x&state={state}", UriKind.Relative));
        request.Headers.Host = HostB;
        request.Headers.Add("X-Forwarded-For", ClientAddress);
        using var callback = await Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false })
            .SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        var record = Assert.Single(await ReadAuditAsync("b", "session.signInRefused"));
        Assert.Equal("anonymous", record.Actor);
        Assert.Null(record.ActorName);
        Assert.Null(record.ApplicationId);
        using var details = JsonDocument.Parse(record.Details);
        Assert.Equal("otherTenant", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal(ClientAddress, details.RootElement.GetProperty("clientAddress").GetString());
        Assert.Empty(await ReadAuditAsync("a"));
    }

    [Fact]
    public async Task A_callback_with_no_readable_state_is_refused_as_an_invalid_state()
    {
        using var client = CreateClient();

        var callback = await SendAsync(client, HttpMethod.Get, "/api/auth/callback?code=x&state=not-a-state");

        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        var record = Assert.Single(await ReadAuditAsync("a", "session.signInRefused"));
        using var details = JsonDocument.Parse(record.Details);
        Assert.Equal("invalidState", details.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_callback_with_an_error_from_the_identity_provider_is_refused_as_a_provider_error()
    {
        using var client = CreateClient();
        var challenge = await SendAsync(client, HttpMethod.Get, "/api/auth/sign-in");
        var state = Regex.Match(challenge.Headers.Location!.Query, "[?&]state=(?<state>[^&]+)").Groups["state"].Value;

        var callback = await SendAsync(client, HttpMethod.Get, $"/api/auth/callback?error=access_denied&state={state}");

        Assert.Equal(HttpStatusCode.InternalServerError, callback.StatusCode);
        var record = Assert.Single(await ReadAuditAsync("a", "session.signInRefused"));
        using var details = JsonDocument.Parse(record.Details);
        Assert.Equal("providerError", details.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task No_audit_record_holds_a_token_the_session_cookie_or_the_user_agent()
    {
        using var client = CreateClient();
        var signIn = await SignInAsync(client, "anna", "/");
        await SendAsync(client, HttpMethod.Post, "/api/auth/sign-out");

        foreach (var tenant in new[] { "a", "b" })
        {
            await using var connection = new NpgsqlConnection(_databases[tenant]);
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand("SELECT row_to_json(a)::text FROM axis.audit_records a", connection);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            while (await reader.ReadAsync(CancellationToken))
            {
                var row = reader.GetString(0);
                Assert.DoesNotMatch(JwtPattern(), row);
                Assert.DoesNotContain(signIn.SessionCookie, row, StringComparison.Ordinal);
                Assert.DoesNotContain(UserAgent, row, StringComparison.Ordinal);
            }
        }

        Assert.Equal(2, (await ReadAuditAsync("a")).Count);
    }

    public async ValueTask InitializeAsync()
    {
        foreach (var tenant in new[] { "a", "b" })
        {
            var connectionString = await database.CreateDatabaseAsync($"signin_{tenant}", CancellationToken);
            _databases[tenant] = connectionString;
            await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        }

        _authority = await keycloak.StartAsync();
        var authority = _authority.ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_databases["a"]);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_databases["a"]));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _databases["a"]);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_databases["b"]));
            builder.UseSetting("Tenants:b:SchemaConnectionString", _databases["b"]);
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", Proxy);
            UseKeycloak(builder, "a", authority);
            UseKeycloak(builder, "b", authority);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ProxyStartupFilter>());
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static void UseKeycloak(IWebHostBuilder builder, string tenant, string authority)
    {
        builder.UseSetting($"Tenants:{tenant}:Oidc:Authority", authority);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientId", KeycloakFixture.ClientId);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
    }

    private HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private HttpClient CreateCookielessClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>The session id that the protected cookie value wraps, the only claim it holds.</summary>
    private string ReadSessionId(string cookieValue)
    {
        var options = Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Session.Scheme);
        var ticket = options.TicketDataFormat.Unprotect(cookieValue);
        Assert.NotNull(ticket);
        var claim = Assert.Single(ticket.Principal.Claims);
        Assert.Equal("Microsoft.AspNetCore.Authentication.Cookies-SessionId", claim.Type);
        return claim.Value;
    }

    /// <summary>
    /// Signs in through the server and Keycloak's login form, and returns where the callback sent
    /// the browser and the session cookie it set.
    /// </summary>
    private async Task<SignInResult> SignInAsync(HttpClient client, string username, string? returnUrl)
    {
        var path = returnUrl is null ? "/api/auth/sign-in" : $"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var challenge = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith($"{Authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", authorize.Query, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", authorize.Query, StringComparison.Ordinal);

        // A separate client plays the browser at Keycloak. Keycloak marks its cookies Secure, which a
        // browser sends to http://localhost but a CookieContainer does not, so the client carries
        // them itself.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var loginPage = await browser.GetAsync(authorize, CancellationToken);
        var html = await loginPage.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(loginPage.StatusCode == HttpStatusCode.OK, html);
        var action = WebUtility.HtmlDecode(FormActionPattern().Match(html).Groups["action"].Value);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(action))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = KeycloakFixture.Password,
                ["credentialId"] = "",
            }),
        };
        loginRequest.Headers.Add("Cookie", string.Join("; ", loginPage.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';')[0])));
        var login = await browser.SendAsync(loginRequest, CancellationToken);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal("http://localhost:5206/api/auth/callback", callback.GetLeftPart(UriPartial.Path));

        var signedIn = await SendAsync(client, HttpMethod.Get, callback.PathAndQuery);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        var sessionCookie = signedIn.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie[..cookie.IndexOf(';', StringComparison.Ordinal)])
            .Single(cookie => cookie.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        return new SignInResult(signedIn.Headers.Location!.OriginalString, sessionCookie[(Session.Scheme.Length + 1)..]);
    }

    private static async Task<HttpResponseMessage> GetMeWithCookieAsync(HttpClient client, string host, string sessionCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Add("Cookie", $"{Session.Scheme}={sessionCookie}");
        return await client.SendAsync(request, CancellationToken);
    }

    /// <summary>
    /// Sends a request to the server and records the response headers and body. A method other
    /// than <c>GET</c> carries the CSRF header unless <paramref name="csrfHeader"/> is false.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, bool csrfHeader = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Forwarded-For", ClientAddress);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (csrfHeader && method != HttpMethod.Get)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        _serverResponses.Add($"{response.Headers}\n{response.Content.Headers}\n{body}");
        return response;
    }

    private async Task<List<AuditRow>> ReadAuditAsync(string tenant, string? action = null)
    {
        await using var connection = new NpgsqlConnection(_databases[tenant]);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            action is null ? AuditSelect : $"{AuditSelect} WHERE action = @action",
            connection);
        if (action is not null)
        {
            command.Parameters.AddWithValue("action", action);
        }

        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4)));
        }

        return rows;
    }

    private async Task<List<SessionRow>> ReadSessionsAsync(string tenant)
    {
        await using var connection = new NpgsqlConnection(_databases[tenant]);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT id_hash, user_id, display_name, roles FROM axis.sessions", connection);
        var rows = new List<SessionRow>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new SessionRow(reader.GetFieldValue<byte[]>(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<string[]>(3)));
        }

        return rows;
    }

    // A JWT is three base64url parts, and its header always starts with eyJ, which is {" encoded.
    [GeneratedRegex(@"eyJ[\w-]+\.[\w-]+\.")]
    private static partial Regex JwtPattern();

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();

    private sealed record SignInResult(string ReturnedTo, string SessionCookie);

    private sealed record SessionRow(byte[] IdHash, string UserId, string DisplayName, string[] Roles);

    private sealed record AuditRow(string Actor, string? ActorName, string Action, Guid? ApplicationId, string Details);

    /// <summary>Gives every request the address of the listed proxy, so the forwarded headers apply.</summary>
    private sealed class ProxyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(Proxy);
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
