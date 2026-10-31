using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Axis.Integration.Tests;

/// <summary>
/// The development realm asks every user for a 6-digit code after the password, as the realms of the tenants
/// do. These tests drive the login page of the <c>axis</c> client directly.
/// </summary>
public sealed class DevelopmentRealmSecondStepTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string Callback = "http://localhost:5206/api/auth/callback";

    private Uri? _authority;

    public async ValueTask InitializeAsync() => _authority = await keycloak.StartAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Asks_for_the_code_after_the_right_password()
    {
        using var browser = NewBrowser();
        var (response, _) = await KeycloakSignIn.SubmitPasswordAsync(browser, AuthorizeAddress(), "requester");

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("twoStep", KeycloakSignIn.Page(html));
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Refuses_a_wrong_code()
    {
        using var browser = NewBrowser();
        var (passwordResponse, cookieHeader) = await KeycloakSignIn.SubmitPasswordAsync(browser, AuthorizeAddress(), "requester");

        using var response = await KeycloakSignIn.SubmitCodeAsync(browser, passwordResponse, cookieHeader, "requester", WrongCode("requester"));

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, html);
        Assert.Equal("twoStep", KeycloakSignIn.Page(html));
        Assert.False(string.IsNullOrEmpty(KeycloakSignIn.Error(html)), html);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Signs_the_user_in_with_the_right_code()
    {
        using var browser = NewBrowser();
        var (passwordResponse, cookieHeader) = await KeycloakSignIn.SubmitPasswordAsync(browser, AuthorizeAddress(), "requester");

        using var response = await KeycloakSignIn.SubmitCodeAsync(browser, passwordResponse, cookieHeader, "requester");

        Assert.True(response.StatusCode == HttpStatusCode.Found, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Callback, response.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.Contains("code=", response.Headers.Location.Query, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DevelopmentRealmTests.Users), MemberType = typeof(DevelopmentRealmTests))]
    public async Task Refuses_the_password_grant_without_the_code(string id, string displayName, string[] roles)
    {
        _ = (displayName, roles);
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = KeycloakFixture.ClientId,
            ["client_secret"] = KeycloakFixture.ClientSecret,
            ["username"] = id,
            ["password"] = KeycloakFixture.Password,
            ["scope"] = "openid",
        });
        using var response = await client.PostAsync(new Uri($"{_authority}/protocol/openid-connect/token"), form, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var error = JsonDocument.Parse(body);
        Assert.Equal("invalid_grant", error.RootElement.GetProperty("error").GetString());
    }

    // Keycloak marks its cookies Secure, so the client carries them itself. See KeycloakSignIn.
    private static HttpClient NewBrowser() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    private Uri AuthorizeAddress() => new(
        $"{_authority}/protocol/openid-connect/auth?client_id={KeycloakFixture.ClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(Callback)}");

    // A code that is none of the codes Keycloak accepts at this moment: the previous, the current and the next one.
    private static string WrongCode(string username)
    {
        var now = DateTimeOffset.UtcNow;
        var accepted = Enumerable.Range(-2, 5).Select(step => DevelopmentTotp.Code(username, now.AddSeconds(30 * step))).ToHashSet(StringComparer.Ordinal);
        return Enumerable.Range(0, 1_000_000).Select(number => number.ToString("D6", CultureInfo.InvariantCulture)).First(code => !accepted.Contains(code));
    }
}
