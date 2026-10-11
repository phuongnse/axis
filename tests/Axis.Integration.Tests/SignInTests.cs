using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in through the development realm in Keycloak, with the code flow and PKCE, and the cookie
/// session it leaves. Tenant a is served at localhost and tenant b at b.example.test, and both sign
/// in at the same realm. The test drives Keycloak's login form as a browser would.
/// </summary>
public sealed partial class SignInTests(KeycloakFixture keycloak) : IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostB = "b.example.test";

    // The realm's client accepts redirects to http://localhost:5206/*, the development server.
    private static readonly Uri _serverAddress = new("http://localhost:5206");

    private readonly List<string> _serverResponses = [];
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

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
        Assert.Equal(["employee"], body.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task Sign_in_without_a_return_address_returns_to_the_root()
    {
        using var client = CreateClient();

        var signIn = await SignInAsync(client, "binh", returnUrl: null);

        Assert.Equal("/", signIn.ReturnedTo);
    }

    [Fact]
    public async Task The_session_holds_only_the_user_and_tenant_and_no_response_carries_a_token()
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

        var options = Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Session.Scheme);
        var ticket = options.TicketDataFormat.Unprotect(signIn.SessionCookie);
        Assert.NotNull(ticket);
        Assert.Equal(
            ["axis:display_name", "axis:role", "axis:tenant_id", "axis:user_id"],
            ticket.Principal.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal));
        Assert.Equal("a", ticket.Principal.FindFirst("axis:tenant_id")?.Value);
        Assert.DoesNotContain(ticket.Properties.Items.Keys, key => key.StartsWith(".Token", StringComparison.Ordinal));
        Assert.DoesNotContain(ticket.Properties.Items.Keys, key => key.Contains("code_verifier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_session_taken_to_another_tenants_host_signs_nobody_in()
    {
        using var client = CreateClient();
        var signIn = await SignInAsync(client, "anna", "/");
        using var cookieless = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = _serverAddress,
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

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
        Assert.StartsWith($"{keycloak.Authority}/protocol/openid-connect/logout?", signOutUrl, StringComparison.Ordinal);
        Assert.Contains("client_id=axis", signOutUrl, StringComparison.Ordinal);
        Assert.Contains($"post_logout_redirect_uri={Uri.EscapeDataString("http://localhost:5206/")}", signOutUrl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    public ValueTask InitializeAsync()
    {
        var authority = keycloak.Authority.ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", UnreachableDatabase);
            UseKeycloak(builder, "a", authority);
            UseKeycloak(builder, "b", authority);
        });
        return ValueTask.CompletedTask;
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
        Assert.StartsWith($"{keycloak.Authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
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

    /// <summary>Sends a request to the server and records the response headers and body.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        _serverResponses.Add($"{response.Headers}\n{response.Content.Headers}\n{body}");
        return response;
    }

    // A JWT is three base64url parts, and its header always starts with eyJ, which is {" encoded.
    [GeneratedRegex(@"eyJ[\w-]+\.[\w-]+\.")]
    private static partial Regex JwtPattern();

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();

    private sealed record SignInResult(string ReturnedTo, string SessionCookie);
}
