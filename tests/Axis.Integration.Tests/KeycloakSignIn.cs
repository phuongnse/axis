using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axis.Integration.Tests;

/// <summary>The browser side of a sign-in through the development Keycloak realm, shared by the tests that sign a person in.</summary>
internal static partial class KeycloakSignIn
{
    /// <summary>
    /// Starts sign-in on the server and logs in at Keycloak's login form as a browser would. Returns the callback
    /// address that Keycloak sends the browser to, which has not been called. <paramref name="record"/> sees the
    /// response of the server to the sign-in request, for a test that checks its responses.
    /// </summary>
    public static async Task<Uri> LogInAsync(
        HttpClient client,
        Uri authority,
        string username,
        Uri expectedBase,
        Func<HttpResponseMessage, Task<HttpResponseMessage>>? record = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/sign-in", UriKind.Relative));
        using var challenge = record is null
            ? await client.SendAsync(request, cancellationToken)
            : await record(await client.SendAsync(request, cancellationToken));
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith($"{authority}/protocol/openid-connect/auth?", authorize.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString($"{expectedBase.GetLeftPart(UriPartial.Authority)}/api/auth/callback"), authorize.Query, StringComparison.Ordinal);

        // A separate client plays the browser at Keycloak. Keycloak marks its cookies Secure, which a
        // browser sends to http://localhost but a CookieContainer does not, so the client carries them itself.
        using var browserHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(browserHandler);
        var (password, cookieHeader) = await SubmitPasswordAsync(browser, authorize, username);
        var login = await SubmitCodeAsync(browser, password, cookieHeader, username);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(cancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal($"{expectedBase.GetLeftPart(UriPartial.Authority)}/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        return callback;
    }

    /// <summary>
    /// Opens the login page at <paramref name="authorize"/> and posts the password of the development user, as a browser
    /// would. Returns the response, which holds the code form, and the Cookie header that the browser carries.
    /// </summary>
    public static async Task<(HttpResponseMessage Response, string CookieHeader)> SubmitPasswordAsync(
        HttpClient browser,
        Uri authorize,
        string username)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var loginPage = await browser.GetAsync(authorize, cancellationToken);
        var html = await loginPage.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(loginPage.StatusCode == HttpStatusCode.OK, html);
        var action = LoginAction(html);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = KeycloakFixture.Password,
                ["credentialId"] = "",
            }),
        };
        var cookieHeader = string.Join("; ", loginPage.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
        loginRequest.Headers.Add("Cookie", cookieHeader);
        return (await browser.SendAsync(loginRequest, cancellationToken), cookieHeader);
    }

    /// <summary>
    /// Answers the code form that follows the password: asserts that <paramref name="passwordResponse"/> holds it, and
    /// posts <paramref name="code"/>, or the user's current code, to the form's action. Returns the response, which
    /// redirects to the callback when the code is right.
    /// </summary>
    public static async Task<HttpResponseMessage> SubmitCodeAsync(
        HttpClient browser,
        HttpResponseMessage passwordResponse,
        string cookieHeader,
        string username,
        string? code = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var html = await passwordResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(passwordResponse.StatusCode == HttpStatusCode.OK && Page(html) == "twoStep", html);
        var action = LoginAction(html);
        // A cookie that the password response sets again replaces the one the browser sent.
        var cookies = cookieHeader.Split("; ").ToDictionary(cookie => cookie.Split('=', 2)[0], cookie => cookie);
        if (passwordResponse.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies.Select(setCookie => setCookie.Split(';')[0]))
            {
                cookies[cookie.Split('=', 2)[0]] = cookie;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["otp"] = code ?? DevelopmentTotp.Code(username),
                ["login"] = "Sign In",
            }),
        };
        request.Headers.Add("Cookie", string.Join("; ", cookies.Values));
        return await browser.SendAsync(request, cancellationToken);
    }

    /// <summary>The address the Axis theme's form posts to: loginAction of the context that login.ftl, login-otp.ftl or login-config-totp.ftl writes as JSON.</summary>
    public static Uri LoginAction(string html) => new(Property(html, "loginAction")!);

    /// <summary>The page the Axis theme draws: "signIn" for the password, "twoStep" for the code, "addAuthenticator" for a new authenticator.</summary>
    public static string Page(string html) => Property(html, "page")!;

    /// <summary>The error that the context shows above the form, or null when it has none.</summary>
    public static string? Error(string html) => Property(html, "error");

    /// <summary>A string field of the context that the Axis theme writes as JSON, or null when it has none.</summary>
    public static string? Property(string html, string name)
    {
        var match = SignInContextPattern().Match(html);
        Assert.True(match.Success, html);
        using var context = JsonDocument.Parse(match.Groups["context"].Value);
        return context.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    [GeneratedRegex("""<script type="application/json" id="axis-sign-in-context">(?<context>.*?)</script>""", RegexOptions.Singleline)]
    private static partial Regex SignInContextPattern();
}
