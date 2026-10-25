using System.Net;
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
        var loginPage = await browser.GetAsync(authorize, cancellationToken);
        var html = await loginPage.Content.ReadAsStringAsync(cancellationToken);
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
        var login = await browser.SendAsync(loginRequest, cancellationToken);
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(cancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal($"{expectedBase.GetLeftPart(UriPartial.Authority)}/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        return callback;
    }

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();
}
