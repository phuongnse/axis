using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Admin;
using Axis.Tenancy.Realms;

namespace Axis.Integration.Tests;

/// <summary>
/// A realm that <see cref="TenantRealm"/> makes in a real Keycloak holds the account rules: passwords of 12 to 128
/// characters that are not on the breached-password list, an authenticator code at every sign-in, a wait after 5 failed
/// sign-ins that does not tell whether the account exists, a password reset link of 30 minutes and no self-registration.
/// </summary>
public sealed partial class TenantRealmAccountRulesTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string ClientId = "account-rules";
    private const string RedirectUri = "http://localhost/callback";
    private const string Password = "a-long-enough-password";
    private const string InvalidCredentials = "Invalid email, username or password.";

    private static readonly TenantRealmSecrets _secrets = new("platform-secret-1", "activation-secret-1", "users-secret-1");

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;
    private string _token = null!;

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
    public async Task The_password_policy_refuses_a_short_password_and_a_breached_one_and_accepts_a_long_one()
    {
        var realm = await NewRealmAsync();
        var userId = await CreateUserAsync(realm, "pat");
        var breached = (await File.ReadAllLinesAsync(
            Path.Combine(AppContext.BaseDirectory, "Keycloak", TenantRealm.BreachedPasswordList), TestContext.Current.CancellationToken))[0];
        Assert.InRange(breached.Length, 12, 128);

        Assert.Equal(HttpStatusCode.BadRequest, await SetPasswordAsync(realm, userId, "elevenchars"));
        Assert.Equal(HttpStatusCode.BadRequest, await SetPasswordAsync(realm, userId, breached));
        Assert.Equal(HttpStatusCode.NoContent, await SetPasswordAsync(realm, userId, Guid.NewGuid().ToString("N")[..12]));
    }

    [Fact]
    public async Task A_new_account_that_gives_its_password_must_add_an_authenticator()
    {
        var realm = await NewRealmAsync();
        await AddClientAsync(realm);
        await EnableEventsAsync(realm);
        var userId = await CreateUserAsync(realm, "pat");
        Assert.Equal(HttpStatusCode.NoContent, await SetPasswordAsync(realm, userId, Password));

        var (action, jar) = await OpenLoginPageAsync(realm);
        var (status, loginHtml, _, location) = await PostLoginAsync(action, jar, "pat", Password);

        // Keycloak sends the browser on to its required action page, not back to the client.
        Assert.True(status == HttpStatusCode.Found, $"{status}\n{await LoginErrorsAsync(realm)}\n{loginHtml}");
        Assert.NotNull(location);
        Assert.False(location.AbsoluteUri.StartsWith(RedirectUri, StringComparison.Ordinal), location.AbsoluteUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.Add("Cookie", CookieHeader(jar));
        using var page = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("addAuthenticator", KeycloakSignIn.Page(html));
    }

    [Fact]
    public async Task A_user_who_must_add_an_authenticator_gets_the_Axis_page_and_adds_one_with_its_fields()
    {
        var realm = await NewRealmAsync();
        await AddClientAsync(realm);
        await EnableEventsAsync(realm);
        var userId = await CreateUserAsync(realm, "pat", requiredActions: ["CONFIGURE_TOTP"]);
        Assert.Equal(HttpStatusCode.NoContent, await SetPasswordAsync(realm, userId, Password));

        var (action, jar) = await OpenLoginPageAsync(realm);
        var (status, loginHtml, _, location) = await PostLoginAsync(action, jar, "pat", Password);
        Assert.True(status == HttpStatusCode.Found, $"{status}\n{await LoginErrorsAsync(realm)}\n{loginHtml}");
        using var open = new HttpRequestMessage(HttpMethod.Get, location);
        open.Headers.Add("Cookie", CookieHeader(jar));
        using var page = await _http.SendAsync(open, TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        Assert.Equal("addAuthenticator", KeycloakSignIn.Page(html));
        var qrCode = KeycloakSignIn.Property(html, "qrCode");
        var manualKey = KeycloakSignIn.Property(html, "manualKey");
        var secret = KeycloakSignIn.Property(html, "secret");
        Assert.False(string.IsNullOrEmpty(qrCode), html);
        Assert.False(string.IsNullOrEmpty(manualKey), html);
        Assert.False(string.IsNullOrEmpty(secret), html);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, Convert.FromBase64String(qrCode)[..8]);

        using var post = new HttpRequestMessage(HttpMethod.Post, KeycloakSignIn.LoginAction(html))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["totp"] = DevelopmentTotp.CodeFromSecret(manualKey.Replace(" ", "", StringComparison.Ordinal)),
                ["totpSecret"] = secret,
                ["userLabel"] = "Phone",
            }),
        };
        post.Headers.Add("Cookie", CookieHeader(jar));
        using var added = await _http.SendAsync(post, TestContext.Current.CancellationToken);
        Assert.True(added.StatusCode == HttpStatusCode.Found, await added.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith(RedirectUri, added.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);

        var credentials = await GetJsonAsync($"admin/realms/{realm}/users/{userId}/credentials");
        Assert.Contains(credentials.EnumerateArray(), credential => credential.GetProperty("type").GetString() == "otp");
    }

    [Fact]
    public async Task After_5_wrong_passwords_the_right_one_gets_the_message_of_an_unknown_account()
    {
        var realm = await NewRealmAsync();
        await AddClientAsync(realm);
        var userId = await CreateUserAsync(realm, "pat");
        Assert.Equal(HttpStatusCode.NoContent, await SetPasswordAsync(realm, userId, Password));

        var (action, jar) = await OpenLoginPageAsync(realm);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (_, failed, next, _) = await PostLoginAsync(action, jar, "pat", "not-the-password-1");
            Assert.Contains(InvalidCredentials, failed, StringComparison.Ordinal);
            await WaitForFailuresAsync(realm, userId, attempt + 1, locked: attempt + 1 == 5);
            action = next;
        }

        var (status, html, afterLock, _) = await PostLoginAsync(action, jar, "pat", Password);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(InvalidCredentials, html, StringComparison.Ordinal);

        var (_, unknown, _, _) = await PostLoginAsync(afterLock, jar, "nobody", Password);
        Assert.Contains(InvalidCredentials, unknown, StringComparison.Ordinal);

        var attack = await GetJsonAsync($"admin/realms/{realm}/attack-detection/brute-force/users/{userId}");
        Assert.True(attack.GetProperty("disabled").GetBoolean());
    }

    [Fact]
    public async Task The_realm_allows_no_registration_and_offers_a_30_minute_password_reset()
    {
        var realm = await NewRealmAsync();
        await AddClientAsync(realm);

        var body = await GetJsonAsync($"admin/realms/{realm}");
        Assert.False(body.GetProperty("registrationAllowed").GetBoolean());
        Assert.True(body.GetProperty("resetPasswordAllowed").GetBoolean());
        Assert.Equal(TenantRealm.PasswordResetLifespanSeconds, body.GetProperty("actionTokenGeneratedByUserLifespan").GetInt32());
        Assert.Equal(1800, TenantRealm.PasswordResetLifespanSeconds);

        using var page = await _http.GetAsync(AuthorizeUri(realm), TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("login-actions/reset-credentials", html, StringComparison.Ordinal);
        Assert.DoesNotContain("login-actions/registration", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_run_leaves_the_second_step_required()
    {
        var realm = await NewRealmAsync();
        await EnsureAsync(realm);

        var executions = await GetJsonAsync($"admin/realms/{realm}/authentication/flows/browser/executions");
        var otp = executions.EnumerateArray().Single(execution =>
            execution.TryGetProperty("providerId", out var provider) && provider.GetString() == "auth-otp-form");
        Assert.Equal("REQUIRED", otp.GetProperty("requirement").GetString());
    }

    [Fact]
    public async Task A_second_run_leaves_the_verify_profile_action_off()
    {
        var realm = await NewRealmAsync();
        await EnsureAsync(realm);

        var action = await GetJsonAsync($"admin/realms/{realm}/authentication/required-actions/VERIFY_PROFILE");
        Assert.False(action.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task A_second_run_leaves_a_first_login_flow_that_only_creates_a_user_and_the_realm_allows_duplicate_emails()
    {
        var realm = await NewRealmAsync();
        await EnsureAsync(realm);

        var flow = Uri.EscapeDataString(RealmSync.FirstLoginFlowAlias);
        var executions = await GetJsonAsync($"admin/realms/{realm}/authentication/flows/{flow}/executions");
        var only = Assert.Single(executions.EnumerateArray());
        Assert.Equal("idp-create-user-if-unique", only.GetProperty("providerId").GetString());
        Assert.Equal("REQUIRED", only.GetProperty("requirement").GetString());
        var flows = await GetJsonAsync($"admin/realms/{realm}/authentication/flows");
        Assert.Equal(1, flows.EnumerateArray().Count(f => f.GetProperty("alias").GetString() == RealmSync.FirstLoginFlowAlias));
        var body = await GetJsonAsync($"admin/realms/{realm}");
        Assert.True(body.GetProperty("duplicateEmailsAllowed").GetBoolean());
        Assert.False(body.GetProperty("loginWithEmailAllowed").GetBoolean());
    }

    [Fact]
    public async Task An_account_whose_username_is_its_email_signs_in_with_its_email_and_password()
    {
        var realm = await NewRealmAsync();
        await AddClientAsync(realm);
        await EnableEventsAsync(realm);
        const string email = "kim@example.test";
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", new
        {
            username = email,
            email,
            enabled = true,
            emailVerified = true,
            firstName = "Kim",
            lastName = "Example",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = created.Headers.Location!.Segments[^1];
        Assert.Equal(HttpStatusCode.NoContent, await SetPasswordAsync(realm, userId, Password));

        var (action, jar) = await OpenLoginPageAsync(realm);
        var (_, wrong, next, _) = await PostLoginAsync(action, jar, email, "not-the-password-1");
        Assert.Contains(InvalidCredentials, wrong, StringComparison.Ordinal);
        await WaitForFailuresAsync(realm, userId, 1, locked: false);

        // The password step passes: Keycloak moves on to the authenticator step instead of the callback.
        var (status, html, _, location) = await PostLoginAsync(next, jar, email, Password);
        Assert.True(status == HttpStatusCode.Found, $"{status}\n{await LoginErrorsAsync(realm)}\n{await BruteForceStateAsync(realm, userId)}\n{html}");
        Assert.NotNull(location);
        Assert.False(location.AbsoluteUri.StartsWith(RedirectUri, StringComparison.Ordinal), location.AbsoluteUri);
    }

    [Fact]
    public async Task A_quick_second_failure_does_not_lock_the_account_before_5_failures()
    {
        var realm = await NewRealmAsync();
        await AddClientAsync(realm);
        await EnableEventsAsync(realm);
        var userId = await CreateUserAsync(realm, "pat");
        Assert.Equal(HttpStatusCode.NoContent, await SetPasswordAsync(realm, userId, Password));

        var body = await GetJsonAsync($"admin/realms/{realm}");
        Assert.Equal(0, body.GetProperty("minimumQuickLoginWaitSeconds").GetInt32());
        Assert.Equal(0, body.GetProperty("quickLoginCheckMilliSeconds").GetInt32());

        // Widen the quick-login check to an hour, so that two failures in a row are quick logins.
        using var widen = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}", new { quickLoginCheckMilliSeconds = 3_600_000 });
        Assert.Equal(HttpStatusCode.NoContent, widen.StatusCode);

        var (action, jar) = await OpenLoginPageAsync(realm);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (_, failed, next, _) = await PostLoginAsync(action, jar, "pat", "not-the-password-1");
            Assert.Contains(InvalidCredentials, failed, StringComparison.Ordinal);
            await WaitForFailuresAsync(realm, userId, attempt + 1, locked: false);
            action = next;
        }

        // The password step passes: Keycloak moves on to the authenticator step instead of locking the account.
        var (status, html, _, location) = await PostLoginAsync(action, jar, "pat", Password);
        Assert.True(status == HttpStatusCode.Found, $"{status}\n{await LoginErrorsAsync(realm)}\n{await BruteForceStateAsync(realm, userId)}\n{html}");
        Assert.NotNull(location);
        Assert.False(location.AbsoluteUri.StartsWith(RedirectUri, StringComparison.Ordinal), location.AbsoluteUri);
    }

    private async Task<string> NewRealmAsync()
    {
        var realm = $"t{Guid.NewGuid():N}"[..16];
        _realms.Add(realm);
        await EnsureAsync(realm);
        return realm;
    }

    private Task EnsureAsync(string realm) =>
        new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(realm, "Acme Corporation", _secrets, TestContext.Current.CancellationToken);

    private async Task AddClientAsync(string realm)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", new
        {
            clientId = ClientId,
            publicClient = true,
            standardFlowEnabled = true,
            redirectUris = new[] { RedirectUri },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Makes the realm record its sign-in events, so that a failed sign-in can be explained by <see cref="LoginErrorsAsync"/>.</summary>
    private async Task EnableEventsAsync(string realm)
    {
        var path = $"admin/realms/{realm}/events/config";
        using var read = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var config = (await read.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken))!.AsObject();
        config["eventsEnabled"] = true;
        using var write = await SendAsync(HttpMethod.Put, path, config);
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
    }

    /// <summary>Lists the failed sign-ins that the realm recorded, each with its error and details, one per line.</summary>
    private async Task<string> LoginErrorsAsync(string realm)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/events?type=LOGIN_ERROR");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return $"LOGIN_ERROR events: {(int)response.StatusCode}";
        }

        var events = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var lines = events.EnumerateArray().Select(entry =>
            $"LOGIN_ERROR {(entry.TryGetProperty("error", out var error) ? error.GetString() : "")} " +
            $"{(entry.TryGetProperty("details", out var details) ? details.GetRawText() : "{}")}").ToList();
        return lines.Count == 0 ? "LOGIN_ERROR events: none" : string.Join('\n', lines);
    }

    /// <summary>Tells how many sign-ins of the user failed and whether Keycloak locked the account, to explain a failed sign-in.</summary>
    private async Task<string> BruteForceStateAsync(string realm, string userId)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/attack-detection/brute-force/users/{userId}");
        var state = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return $"Brute-force state: {(int)response.StatusCode} {state}";
    }

    /// <summary>Waits until Keycloak has counted the failed sign-ins, which it does after the response has gone back.</summary>
    private async Task WaitForFailuresAsync(string realm, string userId, int failures, bool locked)
    {
        var path = $"admin/realms/{realm}/attack-detection/brute-force/users/{userId}";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            using var response = await SendAsync(HttpMethod.Get, path);
            var state = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var attack = JsonDocument.Parse(state);
                if (attack.RootElement.GetProperty("numFailures").GetInt32() == failures
                    && attack.RootElement.GetProperty("disabled").GetBoolean() == locked)
                {
                    return;
                }
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail(
                    $"Expected {failures} failed sign-ins with disabled {locked}. Last brute-force state: {(int)response.StatusCode} {state}\n{await LoginErrorsAsync(realm)}");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private async Task<string> CreateUserAsync(string realm, string username, string[]? requiredActions = null)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", new
        {
            username,
            enabled = true,
            emailVerified = true,
            email = $"{username}@example.test",
            firstName = "Pat",
            lastName = "Example",
            requiredActions = requiredActions ?? [],
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response.Headers.Location!.Segments[^1];
    }

    private async Task<HttpStatusCode> SetPasswordAsync(string realm, string userId, string value)
    {
        using var response = await SendAsync(
            HttpMethod.Put, $"admin/realms/{realm}/users/{userId}/reset-password", new { type = "password", temporary = false, value });
        return response.StatusCode;
    }

    private Uri AuthorizeUri(string realm) => new(_baseAddress,
        $"realms/{realm}/protocol/openid-connect/auth?client_id={ClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&scope=openid");

    /// <summary>Opens the login page as a browser does and returns the address its form posts to and the cookies Keycloak set.</summary>
    private async Task<(string Action, Dictionary<string, string> Jar)> OpenLoginPageAsync(string realm)
    {
        // Keycloak marks its cookies Secure, which a browser sends to http://localhost but a CookieContainer does not.
        using var page = await _http.GetAsync(AuthorizeUri(realm), TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, html);
        var jar = new Dictionary<string, string>();
        Remember(jar, page);
        return (FormAction(html), jar);
    }

    /// <summary>Posts the login form and returns the status, the page, the address that the next form on it posts to and the redirect address.</summary>
    private async Task<(HttpStatusCode Status, string Html, string Action, Uri? Location)> PostLoginAsync(
        string action, Dictionary<string, string> jar, string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(action))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["credentialId"] = "",
            }),
        };
        request.Headers.Add("Cookie", CookieHeader(jar));
        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        Remember(jar, response);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, html, NextFormAction(html), response.Headers.Location);
    }

    private static string CookieHeader(Dictionary<string, string> jar) =>
        string.Join("; ", jar.Select(cookie => $"{cookie.Key}={cookie.Value}"));

    private static void Remember(Dictionary<string, string> jar, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var pair in cookies.Select(cookie => cookie.Split(';')[0]))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            jar[pair[..separator]] = pair[(separator + 1)..];
        }
    }

    // The realm's login page is the Axis theme's, which holds its form address in a JSON block. Keycloak's other pages have a form element.
    private static string FormAction(string html)
    {
        if (html.Contains("id=\"axis-sign-in-context\"", StringComparison.Ordinal))
        {
            return KeycloakSignIn.LoginAction(html).AbsoluteUri;
        }

        var match = FormActionPattern().Match(html);
        Assert.True(match.Success, html);
        return WebUtility.HtmlDecode(match.Groups["action"].Value);
    }

    /// <summary>The address the next form on the page posts to, or an empty string when the page has none.</summary>
    private static string NextFormAction(string html) =>
        html.Contains("id=\"axis-sign-in-context\"", StringComparison.Ordinal) || FormActionPattern().IsMatch(html) ? FormAction(html) : "";

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> MasterTokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();
}
