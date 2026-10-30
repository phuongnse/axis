using System.Net;
using System.Text.RegularExpressions;
using Axis.Configuration.Tests;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// What a person does with an invitation from the realm: reads the emailed link, follows it, sets a password and adds an
/// authenticator app, and signs in at the server's challenge. One instance per test remembers the last authenticator code period,
/// because the realm accepts the code of the current 30 seconds once.
/// </summary>
public sealed partial class InvitationFlow
{
    private long _lastCodePeriod;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static string LinkIn(MailpitMessage message)
    {
        var match = ActionTokenLinkPattern().Match(message.Text);
        Assert.True(match.Success, message.Text);
        return match.Value;
    }

    [GeneratedRegex("""https?://[^\s<>"]*login-actions/action-token[^\s<>"]*""")]
    private static partial Regex ActionTokenLinkPattern();

    // The info page's own link, not the links of its language menu.
    [GeneratedRegex("""id="kc-info-message".*?<a href="(?<address>[^"]*)"+""", RegexOptions.Singleline)]
    private static partial Regex ProceedLinkPattern();


    public static async Task<IReadOnlyList<MailpitMessage>> WaitForMailsAsync(MailpitFixture mailpit, string address, int count)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var messages = await mailpit.MessagesToAsync(address, CancellationToken);
            if (messages.Count >= count)
            {
                Assert.Equal(count, messages.Count);
                return messages;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Mailpit received {messages.Count} of {count} emails for {address} within 30 seconds.");
            await Task.Delay(250, CancellationToken);
        }
    }


    /// <summary>
    /// Opens the emailed link in the browser. Keycloak shows an info page with a link that proceeds, and then the page that sets a password.
    /// Returns the HTML of that page, or null when the link leads somewhere else, such as an error page.
    /// </summary>
    public static async Task<string?> OpenLinkAsync(RealmBrowser browser, string link)
    {
        var html = await FollowAsync(browser, await browser.GetAsync(new Uri(link)));

        for (var hops = 0; hops < 3 && html is not null && !html.Contains("axis-sign-in-context", StringComparison.Ordinal); hops++)
        {
            if (ProceedLinkPattern().Match(html) is not { Success: true } proceed)
            {
                return null;
            }

            html = await FollowAsync(browser, await browser.GetAsync(new Uri(new Uri(link), WebUtility.HtmlDecode(proceed.Groups["address"].Value))));
        }

        return html is not null && html.Contains("axis-sign-in-context", StringComparison.Ordinal) ? html : null;
    }

    // Follows redirects and returns the HTML of the page it ends on, or null when that page is not a 200.
    private static async Task<string?> FollowAsync(RealmBrowser browser, HttpResponseMessage response)
    {
        for (var hops = 0; hops < 5 && response.StatusCode == HttpStatusCode.Found; hops++)
        {
            var location = response.Headers.Location!;
            response.Dispose();
            response = await browser.GetAsync(location);
        }

        using (response)
        {
            return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync(CancellationToken) : null;
        }
    }

    // The invitation asks for a password and an authenticator, in the order that the identity provider chooses.
    public static async Task<bool> ReachesRequiredActionAsync(string link)
    {
        using var browser = new RealmBrowser();
        var html = await OpenLinkAsync(browser, link);
        return html is not null && KeycloakSignIn.Page(html) is "updatePassword" or "addAuthenticator";
    }

    /// <summary>
    /// Follows the link, sets the password and adds an authenticator app. Returns the authenticator's secret. The last page links back
    /// to <paramref name="homeLink"/>, or names no link when it is null, as the invitation of <c>tenant create</c> does.
    /// </summary>
    public async Task<string> SetPasswordAndAuthenticatorAsync(RealmBrowser browser, string link, string password, string? homeLink)
    {
        var html = await OpenLinkAsync(browser, link);
        string? secret = null;
        for (var step = 0; step < 2; step++)
        {
            Assert.NotNull(html);
            var page = KeycloakSignIn.Page(html);
            Dictionary<string, string> form;
            if (page == "updatePassword")
            {
                form = new Dictionary<string, string> { ["password-new"] = password, ["password-confirm"] = password };
            }
            else
            {
                Assert.Equal("addAuthenticator", page);
                secret = KeycloakSignIn.Property(html, "manualKey")!.Replace(" ", "", StringComparison.Ordinal);
                _lastCodePeriod = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
                form = new Dictionary<string, string>
                {
                    ["totp"] = DevelopmentTotp.CodeFromSecret(secret, DateTimeOffset.UtcNow),
                    ["totpSecret"] = KeycloakSignIn.Property(html, "secret")!,
                    ["userLabel"] = "Phone",
                };
            }

            var response = await browser.PostAsync(KeycloakSignIn.LoginAction(html).AbsoluteUri, form);
            if (step == 0)
            {
                html = await FollowAsync(browser, response);
                continue;
            }

            // The last action ends on a page that links back to the address the invitation named, such as the tenant's home.
            using (response)
            {
                var done = await response.Content.ReadAsStringAsync(CancellationToken);
                Assert.True(response.StatusCode == HttpStatusCode.OK, done);
                if (homeLink is not null)
                {
                    Assert.Contains($"href=\"{homeLink}\"", done, StringComparison.Ordinal);
                }
            }
        }

        return secret ?? throw new InvalidOperationException("The invitation did not ask for an authenticator.");
    }


    // The client of the person signs in at the server's challenge, gives the password and a code of the authenticator, and follows the callback.
    public async Task SignInAsync(HttpClient app, string username, string password, string secret)
    {
        using var challenge = await app.GetAsync("/api/auth/sign-in", CancellationToken);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        using var browser = new RealmBrowser();
        using var page = await browser.GetAsync(challenge.Headers.Location!);
        var loginHtml = await page.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, loginHtml);
        using var afterPassword = await browser.PostAsync(
            KeycloakSignIn.LoginAction(loginHtml).AbsoluteUri,
            new Dictionary<string, string> { ["username"] = username, ["password"] = password, ["credentialId"] = "" });
        var html = await afterPassword.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(afterPassword.StatusCode == HttpStatusCode.OK && KeycloakSignIn.Page(html) == "twoStep", html);
        using var login = await browser.PostAsync(
            KeycloakSignIn.LoginAction(html).AbsoluteUri,
            new Dictionary<string, string> { ["otp"] = await NextAuthenticatorCodeAsync(secret), ["login"] = "Sign In" });
        Assert.True(login.StatusCode == HttpStatusCode.Found, await login.Content.ReadAsStringAsync(CancellationToken));
        var callback = login.Headers.Location!;
        Assert.Equal("http://localhost/api/auth/callback", callback.GetLeftPart(UriPartial.Path));
        using var signedIn = await app.GetAsync(callback.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
    }

    // The realm accepts only the code of the current 30 seconds and refuses one it has seen, so a second sign-in waits for the next period.
    public async Task<string> NextAuthenticatorCodeAsync(string secret)
    {
        while (true)
        {
            var now = DateTimeOffset.UtcNow;
            var period = now.ToUnixTimeSeconds() / 30;
            if (period > _lastCodePeriod && now.ToUnixTimeSeconds() % 30 < 27)
            {
                _lastCodePeriod = period;
                return DevelopmentTotp.CodeFromSecret(secret, now);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        }
    }


    /// <summary>
    /// The browser at the realm. Keycloak marks its cookies Secure, which a browser sends to http://localhost but a
    /// CookieContainer does not, so the browser carries them itself.
    /// </summary>
    public sealed class RealmBrowser : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        private readonly Dictionary<string, string> _jar = [];

        public async Task<HttpResponseMessage> GetAsync(Uri address)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            return await SendAsync(request);
        }

        public async Task<HttpResponseMessage> PostAsync(string address, Dictionary<string, string> form)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address)) { Content = new FormUrlEncodedContent(form) };
            return await SendAsync(request);
        }

        public void Dispose() => _client.Dispose();

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_jar.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _jar.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            var response = await _client.SendAsync(request, CancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var pair in setCookies.Select(setCookie => setCookie.Split(';')[0].Split('=', 2)))
                {
                    _jar[pair[0]] = pair[1];
                }
            }

            return response;
        }
    }
}
